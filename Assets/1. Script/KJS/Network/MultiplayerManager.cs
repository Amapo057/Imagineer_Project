using System;
using UnityEngine;

// Scene-level game synchronization; the persistent GameNetworkManager owns the connection.
public sealed class MultiplayerManager : MonoBehaviour
{
    [SerializeField] private CardDatabase cardDatabase;
    public static MultiplayerManager Current { get; private set; }
    public event Action<string> OnLog;
    GameNetworkManager network;
    GameManager reportedMatch;
    int reportedRemaining, reportedFatigue;

    public static MultiplayerManager GetOrCreate(CardDatabase database)
    {
        var manager = Current != null ? Current : FindFirstObjectByType<MultiplayerManager>();
        if (manager == null)
        {
            var root = GameObject.Find("GameManager");
            var go = new GameObject("Multiplayer Manager");
            if (root != null) go.transform.SetParent(root.transform, false);
            manager = go.AddComponent<MultiplayerManager>();
        }
        manager.Initialize(database);
        return manager;
    }

    void Awake() => Initialize(cardDatabase);

    void Initialize(CardDatabase database)
    {
        if (database != null) cardDatabase = database;
        if (network != null && database != null) network.ConfigureMatch(database);
        if (network != null) return;
        Current = this;
        network = GameNetworkManager.Instance;
        if (cardDatabase != null) network.ConfigureMatch(cardDatabase);
        network.OnGameStartReceived += StartGame;
        network.OnEndTurnReceived += ReceiveEndTurn;
        network.OnCardPlayedReceived += ReceiveCard;
        network.OnSpellCastReceived += ReceiveSpell;
        network.OnDrawConfirmedReceived += ReceiveDraw;
        network.OnDeckRemainingReceived += ReceiveDeckRemaining;
        network.OnCoinUsedReceived += ReceiveCoin;
        network.OnRematchReceived += ResetGame;
        network.OnDisconnected += Disconnected;
    }

    void StartGame(PlayerSide first)
    {
        VRGameSession.ApplyNetworkFirstPlayer(first, ProductionGameController.Current?.Deck != null);
        OnLog?.Invoke(first == PlayerSide.Me ? "네트워크 연결 - 내가 선공" : "네트워크 연결 - 상대가 선공");
    }

    void ReceiveEndTurn()
    {
        ProductionGameController.Current?.TableFeedback?.BindGame();
        if (!VRGameSession.Game.IsGameOver) VRGameSession.Game.EndTurn();
    }

    void ReceiveCard(int cardId, int lane)
    {
        var data = cardDatabase != null ? cardDatabase.GetCardById(cardId) : null;
        var game = VRGameSession.Game;
        var board = game.Board.opponent;
        if (data == null || data.cardType != CardType.Unit || game.IsGameOver ||
            lane < 0 || lane >= board.lanes.Length || board.lanes[lane] != null) return;
        var card = new CardInstance(data, PlayerSide.Opponent);
        if (!board.TryPlaceOnLane(card, lane)) return;
        board.currentCost -= data.cost;
        VRGameSession.ConsumePhysicalCard(PlayerSide.Opponent);
        if (data.HasKeyword(CardKeyword.Draw)) game.DrawCards(PlayerSide.Opponent, 1);
        if (data.HasKeyword(CardKeyword.Heal)) game.ApplyHealToLeftAlly(PlayerSide.Opponent, lane, card.currentAttack);
        if (data.HasKeyword(CardKeyword.FrontDamage)) game.ApplyFrontDamage(PlayerSide.Opponent, lane, card.currentAttack);
        ProductionGameController.Current?.ArmWeapon(PlayerSide.Opponent, data);
    }

    void ReceiveSpell(int id, int lane)
    {
        if (cardDatabase != null && VRGameSession.TryCastPhysicalSpell(cardDatabase.GetCardById(id), PlayerSide.Opponent, lane))
            OnLog?.Invoke("상대 마법 사용");
    }
    void ReceiveDraw() => VRGameSession.ConfirmPhysicalDraw(PlayerSide.Opponent);
    void ReceiveDeckRemaining(int count, int fatigue) => VRGameSession.ApplyPhysicalDeckState(count, fatigue);

    void LateUpdate()
    {
        var flow = ProductionGameController.Current;
        if (flow == null || flow.Deck == null || !flow.Ready || !network.Connected) return;
        int remaining = flow.Deck.RemainingCount;
        int fatigue = flow.Game.Board.me.fatigueDamage;
        if (reportedMatch == flow.Game && remaining == reportedRemaining && fatigue == reportedFatigue) return;
        if (!network.SendDeckRemaining(remaining, fatigue)) return;
        reportedMatch = flow.Game;
        reportedRemaining = remaining;
        reportedFatigue = fatigue;
    }
    void ReceiveCoin() { if (VRGameSession.Coin.TryUse(PlayerSide.Opponent)) OnLog?.Invoke("상대 동전 사용"); }
    void Disconnected() => OnLog?.Invoke("연결 끊김 - 재접속 시도 중");
    void ResetGame()
    {
        VRGameSession.ResetGame(ProductionGameController.Current?.Deck != null);
        OnLog?.Invoke("재대결 시작");
    }

    public void EndTurn()
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return;
        if (!VRGameSession.NetworkPlayAllowed || VRGameSession.Game.IsGameOver) return;
        if (network.Connected && !network.TrySendEndTurn(PlayerSide.Me, VRGameSession.Game.State.CurrentPlayer)) return;
        ProductionGameController.Current?.TableFeedback?.BindGame();
        VRGameSession.Game.EndTurn();
        if (network.DeveloperMode) StartCoroutine(EndDeveloperOpponentTurn());
    }

    System.Collections.IEnumerator EndDeveloperOpponentTurn()
    {
        var game = VRGameSession.Game;
        while (game == VRGameSession.Game && ProductionGameController.Current?.CombatPlaying == true)
            yield return null;
        if (!network.DeveloperMode || game != VRGameSession.Game) yield break;

        Array.Clear(game.Board.me.lanes, 0, game.Board.me.lanes.Length);
        if (!game.IsGameOver && game.State.CurrentPlayer == PlayerSide.Opponent)
        {
            var flow = ProductionGameController.Current;
            if (flow != null) flow.PassDeveloperOpponentTurn();
            else game.EndTurn();
        }
    }

    public void UseCoin()
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return;
        if (!VRGameSession.NetworkPlayAllowed || !VRGameSession.Coin.TryUse(PlayerSide.Me)) return;
        if (network.Connected) network.SendCoinUsed();
        OnLog?.Invoke("동전 사용: 이번 턴 코스트 +1");
    }

    public bool TryUseSpell(int cardId, int lane = -1)
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return false;
        if (!VRGameSession.NetworkPlayAllowed || cardDatabase == null ||
            !VRGameSession.TryCastPhysicalSpell(cardDatabase.GetCardById(cardId), PlayerSide.Me, lane)) return false;
        if (network.Connected) network.SendSpellCast(cardId, lane);
        ProductionGameController.Current?.Deck?.RecordPlayedCard(cardId);
        return true;
    }

    public void ConfirmDraw() => TryConfirmDraw();

    public bool TryConfirmDraw()
    {
        var flow = ProductionGameController.Current;
        if (VRGameSession.RequireNetwork && flow != null && !flow.HardwareReady) return false;
        if (flow?.CombatPlaying == true || !VRGameSession.NetworkPlayAllowed || !VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me)) return false;
        if (network.Connected) network.SendDrawConfirmed();
        return true;
    }

    public void Rematch()
    {
        if (ProductionGameController.Current?.CombatPlaying == true || !VRGameSession.NetworkPlayAllowed) return;
        if (network.Connected) network.SendRematch();
        ResetGame();
    }

    void OnDestroy()
    {
        if (Current == this) Current = null;
        if (network == null) return;
        network.OnGameStartReceived -= StartGame;
        network.OnEndTurnReceived -= ReceiveEndTurn;
        network.OnCardPlayedReceived -= ReceiveCard;
        network.OnSpellCastReceived -= ReceiveSpell;
        network.OnDrawConfirmedReceived -= ReceiveDraw;
        network.OnDeckRemainingReceived -= ReceiveDeckRemaining;
        network.OnCoinUsedReceived -= ReceiveCoin;
        network.OnRematchReceived -= ResetGame;
        network.OnDisconnected -= Disconnected;
    }
}

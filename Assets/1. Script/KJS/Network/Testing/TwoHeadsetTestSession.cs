using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Diagnostic input adapter. It uses the production session/rules, without camera/marker dependencies.
[DefaultExecutionOrder(-500)]
public sealed class TwoHeadsetTestSession : MonoBehaviour
{
    [Serializable] public sealed class Snapshot { public string build; public Entry[] cards; }
    [Serializable] public sealed class Entry { public int id, cost, attack, health, type, keywords; public string name; }
    public TextAsset cardSnapshot;
    public string BuildLabel { get; private set; } = "";
    public CardDatabase Database { get; private set; }
    public GameNetworkManager Network { get; private set; }
    public bool Ready { get; private set; }
    public int Starts { get; private set; }
    public string LastAction { get; private set; } = "같은 APK를 두 기기에서 실행하세요.";
    public GameManager Game => VRGameSession.Game;
    public bool CanAct => Ready && Network.Connected && !Game.IsGameOver && Game.State.CurrentPlayer == PlayerSide.Me;
    readonly HashSet<int> usedUnits = new HashSet<int>();
    readonly List<CardData> ownedCards = new List<CardData>();
    bool initialized;

    void Start() => Initialize();
    public void Initialize()
    {
        if (initialized) return;
        if (cardSnapshot == null) throw new InvalidOperationException("테스트 카드 스냅샷이 없습니다. 테스트 씬 생성 메뉴를 실행하세요.");
        var source = JsonUtility.FromJson<Snapshot>(cardSnapshot.text);
        if (source.cards == null || source.cards.Length != 27) throw new InvalidOperationException("카드 27종 스냅샷이 필요합니다.");
        Database = ScriptableObject.CreateInstance<CardDatabase>();
        foreach (var item in source.cards)
        {
            var card = ScriptableObject.CreateInstance<CardData>();
            card.cardId = item.id; card.cardName = item.name; card.cost = item.cost;
            card.attack = item.attack; card.health = item.health;
            card.cardType = (CardType)item.type; card.keywords = (CardKeyword)item.keywords;
            Database.allCards.Add(card); ownedCards.Add(card);
        }
        BuildLabel = source.build;
        Network = GetComponent<GameNetworkManager>();
        if (Network == null) Network = gameObject.AddComponent<GameNetworkManager>();
        Network.ConfigureMatch(Database, "TWO_HEADSET_DIAGNOSTIC");
        Network.OnGameStartReceived += OnStart;
        Network.OnDisconnected += OnDisconnected;
        Network.OnEndTurnReceived += OnRemoteEndTurn;
        Network.OnCardPlayedReceived += OnRemoteCard;
        Network.OnCoinUsedReceived += OnRemoteCoin;
        Network.OnSpellCastReceived += OnRemoteSpell;
        Network.OnDrawConfirmedReceived += OnRemoteDraw;
        Network.OnRematchReceived += OnRemoteRematch;
        initialized = true; ResetLocal();
        Debug.Log($"[LAN Test] Build={BuildLabel} Signature={Network.MatchSignature} IP={Network.LocalAddresses}");
    }

    void Record(string text) { LastAction = text; Debug.Log("[LAN Test] " + text); }
    void ResetLocal() { usedUnits.Clear(); VRGameSession.ResetGame(); }
    void OnStart(PlayerSide first)
    {
        usedUnits.Clear(); VRGameSession.ApplyNetworkFirstPlayer(first); Ready = true; Starts++;
        Record(first == PlayerSide.Me ? "새 게임: 내가 선공입니다." : "새 게임: 내가 후공입니다.");
    }
    void OnDisconnected() { Ready = false; Record("연결이 끊겼습니다. 재연결하면 새 게임으로 시작합니다."); }
    void OnRemoteEndTurn() { if (Ready && !Game.IsGameOver) { Game.EndTurn(); Record("상대 턴 종료 수신"); } }
    void OnRemoteCard(int id, int lane) => ApplyUnit(id, lane, PlayerSide.Opponent);
    void OnRemoteCoin() { if (VRGameSession.Coin.TryUse(PlayerSide.Opponent)) Record("상대 동전 사용 수신"); }
    void OnRemoteSpell(int id, int lane)
    {
        if (VRGameSession.TryCastPhysicalSpell(Database.GetCardById(id), PlayerSide.Opponent, lane)) Record("상대 마법 수신: " + id);
    }
    void OnRemoteDraw() => VRGameSession.ConfirmPhysicalDraw(PlayerSide.Opponent);
    void OnRemoteRematch() { Ready = false; ResetLocal(); Record("재대결 요청 수신"); }

    bool ApplyUnit(int id, int lane, PlayerSide side)
    {
        var card = Database.GetCardById(id); var board = Game.Board.GetBoard(side);
        int key = (int)side * 100 + id;
        if (!Ready || Game.IsGameOver || Game.State.CurrentPlayer != side || card == null || card.cardType != CardType.Unit ||
            lane < 0 || lane >= 4 || board.lanes[lane] != null || board.currentCost < card.cost || usedUnits.Contains(key) ||
            VRGameSession.PhysicalHandCount(side) <= 0) return false;
        board.lanes[lane] = new CardInstance(card, side); board.currentCost -= card.cost; usedUnits.Add(key);
        VRGameSession.ConsumePhysicalCard(side);
        if (card.HasKeyword(CardKeyword.Draw)) Game.DrawCards(side, 1);
        if (card.HasKeyword(CardKeyword.Heal)) Game.ApplyHealToLeftAlly(side, lane, card.attack);
        if (card.HasKeyword(CardKeyword.FrontDamage)) Game.ApplyFrontDamage(side, lane, card.attack);
        Record((side == PlayerSide.Me ? "내" : "상대") + $" 가상 카드 {id}, {lane + 1}번 라인 배치");
        return true;
    }
    public void PlaceTestCard()
    {
        if (!CanAct) { Record("연결 후 내 턴에 사용하세요."); return; }
        // Common units with no weapon input. Each physical card is used only once per match.
        foreach (int id in new[] { 1, 3, 5 })
        {
            for (int lane = 0; lane < 4; lane++)
                if (ApplyUnit(id, lane, PlayerSide.Me)) { Network.SendCardPlayed(id, lane); return; }
        }
        Record("낼 수 있는 테스트 카드가 없습니다. 코스트·빈 라인·사용한 카드(1·3·5)를 확인하세요.");
    }
    public void EndTurn()
    {
        if (!CanAct || !Network.TrySendEndTurn(PlayerSide.Me, Game.State.CurrentPlayer)) return;
        Game.EndTurn(); Record("내 턴 종료 전송");
    }
    public void UseCoin()
    {
        if (!CanAct || !VRGameSession.Coin.TryUse(PlayerSide.Me)) { Record("동전은 후공의 자기 턴에 한 번만 사용합니다."); return; }
        Network.SendCoinUsed(); Record("동전 사용 전송");
    }
    public void CastSpell(int id)
    {
        if (!CanAct || !VRGameSession.TryCastPhysicalSpell(Database.GetCardById(id), PlayerSide.Me))
        { Record("마법 사용 불가: 내 턴·코스트·이미 사용한 카드인지 확인하세요."); return; }
        Network.SendSpellCast(id, -1); Record("마법 전송: " + id);
    }
    public void ConfirmDraw()
    {
        if (!Ready || !Network.Connected || !VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me)) return;
        Network.SendDrawConfirmed(); Record("카드 1장 뽑기 확인 전송");
    }
    public void Rematch()
    {
        if (!Ready || !Network.Connected) return;
        Network.SendRematch(); Ready = false; ResetLocal(); Record("재대결 요청 전송");
    }

    public string StateCode()
    {
        if (!Ready || !Network.Connected) return "------";
        var hostSide = Network.IsHost ? PlayerSide.Me : PlayerSide.Opponent;
        var clientSide = hostSide == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me;
        var text = new StringBuilder().Append(Game.TurnNumber).Append('|')
            .Append(Game.State.CurrentPlayer == hostSide ? 'H' : 'C').Append('|').Append(Game.IsGameOver);
        foreach (var side in new[] { hostSide, clientSide })
        {
            var board = Game.Board.GetBoard(side);
            text.Append('|').Append(board.heroDamage).Append(':').Append(board.currentCost).Append(':').Append(board.maxCost)
                .Append(':').Append(board.ownTurnCount).Append(':').Append(board.isSecondPlayer)
                .Append(':').Append(VRGameSession.Coin.IsAvailable(side)).Append(':').Append(VRGameSession.PendingPhysicalDraws(side));
            foreach (var card in board.lanes)
                if (card == null) text.Append("/-");
                else text.Append('/').Append(card.data.cardId).Append(':').Append(card.currentAttack).Append(':').Append(card.currentHealth).Append(':').Append(card.tempAttackBonus);
        }
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").Substring(0, 8);
    }

    void OnDestroy()
    {
        if (Network != null)
        {
            Network.OnGameStartReceived -= OnStart; Network.OnDisconnected -= OnDisconnected;
            Network.OnEndTurnReceived -= OnRemoteEndTurn; Network.OnCardPlayedReceived -= OnRemoteCard;
            Network.OnCoinUsedReceived -= OnRemoteCoin; Network.OnSpellCastReceived -= OnRemoteSpell;
            Network.OnDrawConfirmedReceived -= OnRemoteDraw; Network.OnRematchReceived -= OnRemoteRematch;
        }
        if (!initialized) return;
        VRGameSession.ResetGame();
        foreach (var card in ownedCards) if (Application.isPlaying) Destroy(card); else DestroyImmediate(card);
        if (Application.isPlaying) Destroy(Database); else DestroyImmediate(Database);
    }
}

using System.Collections.Generic;
using UnityEngine;

// Production input adapter: physical cards use this same session as the existing HUD.
[DefaultExecutionOrder(-400)]
public sealed class ProductionGameController : MonoBehaviour
{
    public CardDatabase Database;
    public GameHUDController hud;
    public MarkerDetecter detector;
    public AnchorManager anchor;
    public ProductionSeating seating;
    public static ProductionGameController Current { get; private set; }
    public bool HardwareReady => (detector == null || detector.CameraReady) && (anchor == null || anchor.IsReady) && (seating == null || seating.IsReady);
    public GameNetworkManager Network { get; private set; }
    public GameManager Game => VRGameSession.Game;
    public bool Ready { get; private set; }
    public bool CanAct => HardwareReady && Ready && Network.Connected && !Game.IsGameOver && Game.State.CurrentPlayer == PlayerSide.Me;
    public int Starts { get; private set; }
    public string BuildLabel => Application.version;
    public string LastAction { get; private set; } = "같은 APK를 실행하고 중앙 마커 0번으로 각자의 테이블을 맞추세요.";
    readonly HashSet<int> used = new HashSet<int>();
    bool initialized;
    public void Initialize()
    {
        if (initialized) return;
        initialized = true; Current = this;
        VRGameSession.RequireNetwork = true; VRGameSession.NetworkReady = false;
        Network = GameNetworkManager.Instance;
        Network.ConfigureMatch(Database);
        Network.OnGameStartReceived += OnStarted;
        Network.OnDisconnected += OnDisconnected;
        Network.OnRematchReceived += OnReset;
        if (Network.Connected) Network.RetryConnection();
    }
    void Awake() => Initialize();
    void OnStarted(PlayerSide first) { Ready = true; VRGameSession.NetworkReady = true; used.Clear(); Starts++; LastAction = "연결 완료. 실물 카드를 내 필드에 놓으세요."; }
    void OnDisconnected() { Ready = false; VRGameSession.NetworkReady = false; LastAction = "연결이 끊겼습니다. 재연결 뒤 새 판으로 시작합니다."; }
    void OnReset() { Ready = false; VRGameSession.NetworkReady = false; used.Clear(); }
    public void EndTurn() { if (CanAct) hud.OnEndTurnButtonClicked(); }
    public void UseCoin() { if (CanAct) hud.OnCoinCardUsed(); }
    public void ConfirmDraw() { if (HardwareReady && Ready && Network.Connected) hud.ConfirmDrawnCard(); }
    public void Rematch() { if (!Ready || !Network.Connected) return; hud.OnRematchButtonClicked(); OnReset(); }
    public bool TryPlacePhysicalCard(int id, int lane)
    {
        if (!CanAct) { LastAction = "연결 완료 후 내 턴에 카드를 놓으세요."; return false; }
        var data = Database.GetCardById(id);
        if (data == null || lane < 0 || lane >= GameRules.LaneCount) return false;
        if (data.cardType == CardType.Spell)
        {
            bool cast = hud.TryUseSpell(id);
            LastAction = cast ? data.cardName + " 사용" : "이미 사용한 마법이거나 코스트가 부족합니다.";
            return cast;
        }
        var board = Game.Board.me;
        if (used.Contains(id) || board.lanes[lane] != null || board.currentCost < data.cost)
        { LastAction = "빈 라인·코스트·이미 사용한 카드를 확인하세요."; return false; }
        var card = new CardInstance(data, PlayerSide.Me);
        if (!board.TryPlaceOnLane(card, lane)) return false;
        board.currentCost -= data.cost; used.Add(id);
        Network.SendCardPlayed(id, lane);
        if (data.HasKeyword(CardKeyword.Draw)) Game.DrawCards(PlayerSide.Me, 1);
        if (data.HasKeyword(CardKeyword.Heal)) Game.ApplyHealToLeftAlly(PlayerSide.Me, lane, card.currentAttack);
        if (data.HasKeyword(CardKeyword.FrontDamage)) Game.ApplyFrontDamage(PlayerSide.Me, lane, card.currentAttack);
        LastAction = data.cardName + " 배치"; return true;
    }
    void OnDestroy()
    {
        if (!initialized) return;
        if (Current == this) Current = null;
        Network.OnGameStartReceived -= OnStarted; Network.OnDisconnected -= OnDisconnected; Network.OnRematchReceived -= OnReset;
        VRGameSession.RequireNetwork = false; VRGameSession.NetworkReady = false;
    }
}




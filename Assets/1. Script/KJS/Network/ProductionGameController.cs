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
    internal ProductionTableFeedback TableFeedback { get; set; }
    public bool CombatPlaying => TableFeedback != null && TableFeedback.IsPlaying;
    public bool Ready { get; private set; }
    public bool CanAct => HardwareReady && Ready && Network.Connected && !CombatPlaying && !Game.IsGameOver && Game.State.CurrentPlayer == PlayerSide.Me;
    public int Starts { get; private set; }
    public string BuildLabel => Application.version;
    public string LastAction { get; private set; } = "같은 APK를 실행하고 중앙 마커 0번으로 각자의 테이블을 맞추세요.";
    public string ReadinessHint => !Ready || !Network.Connected ? "상대를 연결해 주세요. 같은 Wi-Fi에서 자동으로 찾습니다." :
        detector != null && !detector.CameraReady ? "카메라를 준비하고 있습니다. 카메라 권한을 확인해 주세요." :
        anchor != null && !anchor.IsReady ? "중앙 0번 마커를 3.5초 바라봐 테이블을 맞춰 주세요." :
        seating != null && !seating.IsReady ? "테이블 한쪽에 자리 잡아 주세요. 내 필드를 맞추고 있습니다." : "";
    readonly HashSet<int> used = new HashSet<int>();
    ProductionWeaponState weapons = new ProductionWeaponState();
    public int PendingWeapons => Ready ? weapons.Pending(Game, PlayerSide.Me) : 0;
    public void ArmWeapon(PlayerSide side, CardData card) => weapons.Arm(Game, side, card);
    public bool TryStrikeWeapon(int lane)
    {
        if (!CanAct) return false;
        int source = weapons.Source(Game, PlayerSide.Me), turn = Game.TurnNumber;
        if (!weapons.CanStrike(Game, PlayerSide.Me, source, lane, turn)) return false;
        if (!Network.SendWeaponStrike(source, lane, turn)) { LastAction = "무기 타격을 전송하지 못했습니다. 연결을 확인해 주세요."; return false; }
        bool hit = weapons.Strike(Game, PlayerSide.Me, source, lane, turn);
        if (hit) { LastAction = $"무기로 상대 {lane + 1}번 칸을 타격했습니다."; GetComponent<ProductionGameUI>()?.ShowFeedback(LastAction); GetComponent<ProductionTableFeedback>()?.WeaponImpact(PlayerSide.Opponent, lane); }
        return hit;
    }
    void ReceiveWeapon(int source, int lane, int turn)
    {
        if (!Ready || !Network.Connected) return;
        if (weapons.Strike(Game, PlayerSide.Opponent, source, lane, turn))
        {
            GetComponent<ProductionGameUI>()?.ShowFeedback($"상대 무기가 내 {lane + 1}번 칸을 타격했습니다!");
            GetComponent<ProductionTableFeedback>()?.WeaponImpact(PlayerSide.Me, lane);
        }
    }
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
        Network.OnWeaponStrikeReceived += ReceiveWeapon;
        if (Network.Connected) Network.RetryConnection();
    }
    void Awake()
    {
        Initialize();
        if (GetComponent<AutomaticPhysicalDraw>() == null) gameObject.AddComponent<AutomaticPhysicalDraw>();
    }
    void OnStarted(PlayerSide first) { Ready = true; VRGameSession.NetworkReady = true; used.Clear(); weapons = new ProductionWeaponState(); Starts++; LastAction = "연결 완료. 실물 카드를 내 필드에 놓으세요."; }
    void OnDisconnected() { Ready = false; VRGameSession.NetworkReady = false; LastAction = "연결이 끊겼습니다. 재연결 뒤 새 판으로 시작합니다."; }
    void OnReset() { Ready = false; VRGameSession.NetworkReady = false; used.Clear(); weapons = new ProductionWeaponState(); }
    public void EndTurn() { if (CanAct) hud.OnEndTurnButtonClicked(); }
    public void UseCoin() { if (CanAct) hud.OnCoinCardUsed(); }
    public void ConfirmDraw() { if (HardwareReady && Ready && Network.Connected && !CombatPlaying) hud.ConfirmDrawnCard(); }
    public void Rematch() { if (!Ready || !Network.Connected || CombatPlaying) return; hud.OnRematchButtonClicked(); OnReset(); }
    public bool TryPlacePhysicalCard(int id, int lane)
    {
        if (!CanAct) { LastAction = ReadinessHint.Length > 0 ? ReadinessHint : Game.IsGameOver ? "게임이 끝났습니다. 재대결을 선택해 주세요." : "상대 턴입니다. 내 턴에 카드를 놓아 주세요."; return false; }
        var data = Database.GetCardById(id);
        if (data == null || lane < 0 || lane >= GameRules.LaneCount) return false;
        if (data.cardType == CardType.Spell)
        {
            bool cast = hud.TryUseSpell(id);
            LastAction = cast ? data.cardName + " 사용" : "이미 사용한 마법이거나 코스트가 부족합니다.";
            return cast;
        }
        var board = Game.Board.me;
        if (used.Contains(id)) { LastAction = data.cardName + ": 이번 판에 이미 사용한 카드입니다."; return false; }
        if (board.lanes[lane] != null) { LastAction = (lane + 1) + "번 칸에 기물이 있습니다. 빈 칸에 놓아 주세요."; return false; }
        if (board.currentCost < data.cost) { LastAction = $"코스트가 {data.cost - board.currentCost} 부족합니다. 필요 {data.cost} / 남음 {board.currentCost}"; return false; }
        if (VRGameSession.PhysicalHandCount(PlayerSide.Me) <= 0) { LastAction = "먼저 실물 카드를 뽑고 뽑기 확인을 눌러 주세요."; return false; }
        var card = new CardInstance(data, PlayerSide.Me);
        if (!board.TryPlaceOnLane(card, lane)) return false;
        board.currentCost -= data.cost; used.Add(id);
        VRGameSession.ConsumePhysicalCard(PlayerSide.Me);
        Network.SendCardPlayed(id, lane);
        if (data.HasKeyword(CardKeyword.Draw)) Game.DrawCards(PlayerSide.Me, 1);
        if (data.HasKeyword(CardKeyword.Heal)) Game.ApplyHealToLeftAlly(PlayerSide.Me, lane, card.currentAttack);
        if (data.HasKeyword(CardKeyword.FrontDamage)) Game.ApplyFrontDamage(PlayerSide.Me, lane, card.currentAttack);
        ArmWeapon(PlayerSide.Me, data);
        LastAction = data.cardName + " 배치"; return true;
    }
    void OnDestroy()
    {
        if (!initialized) return;
        if (Current == this) Current = null;
        Network.OnGameStartReceived -= OnStarted; Network.OnDisconnected -= OnDisconnected; Network.OnRematchReceived -= OnReset;
        Network.OnWeaponStrikeReceived -= ReceiveWeapon;
        VRGameSession.RequireNetwork = false; VRGameSession.NetworkReady = false;
    }
}




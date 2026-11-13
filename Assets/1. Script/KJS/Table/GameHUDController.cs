using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR 플레이 화면 HUD 전체를 관리하는 컨트롤러. 하나의 Canvas(VRGameHUD, CenterEyeAnchor 아래에
/// 매달려 있어서 "Debug Canvas"처럼 항상 시야를 따라옴)에 붙어서 아래 네 가지를 담당함:
///   1) 코스트 표시 (화면 왼쪽 아래, "현재/최대")
///   2) 체력 표시 (화면 오른쪽 아래, "10에서 명치 맞은 횟수를 뺀 남은 목숨 / 10")
///   3) End Turn 처리 — 테이블 위 물리 "End Turn" 버튼(OnClick)에서 OnEndTurnButtonClicked()를 호출함
///   4) 로그 창 — 화면 오른쪽 위, 머리를 따라오는 토글 버튼을 누르면 켜고 끄는 로그 패널
///   5) 재대결 — OnRematchButtonClicked(). End Turn처럼 아직 물리 버튼은 없고 [ContextMenu]로만
///      테스트 가능함. 네트워크 연결 중이면 양쪽 다 깨끗한 새 게임으로 리셋됨(VRGameSession.ResetGame)
///
/// 예전에 있던 TurnController.cs(End Turn 전용, 디버그 텍스트만 있던 버전)를 대체함 — End Turn
/// 처리 로직은 그대로고, 코스트/체력/로그가 추가됨.
///
/// 지금 일부러 안 만든 두 가지 (사용자가 따로 구상 중이라 보류):
///   - 테이블을 중앙 마커 기준으로 고정하는 부분
///   - 선공/후공을 정하는 연출(애니메이션)
/// 두 가지 모두 나중에 들어와도 이 스크립트는 그대로 재사용 가능함 — 선공/후공 쪽은
/// OnFirstPlayerDetermined를 연출 코드 끝에서 호출해주면 로그에 바로 반영됨
/// </summary>
public class GameHUDController : MonoBehaviour
{
    [Header("코스트 / 체력 표시 (왼쪽 아래 / 오른쪽 아래)")]
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("로그 창 (오른쪽 위 토글)")]
    [SerializeField] private GameObject logPanel;
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private Button logToggleButton;
    [Tooltip("로그에 쌓아둘 최대 줄 수 — 넘으면 오래된 줄부터 지움")]
    [SerializeField] private int maxLogLines = 30;

    [Header("네트워크로 받은 상대 카드 반영용")]
    [Tooltip("상대가 낸 카드(cardId)를 찾기 위한 데이터베이스. SlotModelSpawner와 같은 에셋을 " +
             "끌어다 놓으면 됨 — Assets/2. Data/Card/CardDatabase.asset")]
    [SerializeField] private CardDatabase cardDatabase;

    [Header("상대 필드 표시 (물리 슬롯이 없어서 텍스트로 대신 보여줌)")]
    [Tooltip("상대 필드(라인 4개) 상황을 한 줄로 보여주는 텍스트. 전용 변경 이벤트가 없어서 " +
             "코스트/체력처럼 매 프레임 폴링해서 갱신함(UpdateOpponentFieldText 참고)")]
    [SerializeField] private TextMeshProUGUI opponentFieldText;

    [Header("승패 결과 화면")]
    [Tooltip("게임이 끝났을 때만 켜지는 결과 패널(루트 오브젝트). 평소엔 꺼둠")]
    [SerializeField] private GameObject resultPanel;
    [Tooltip("승리/패배/무승부 문구를 보여줄 텍스트. resultPanel 밑에 있어야 함")]
    [SerializeField] private TextMeshProUGUI resultText;


    private readonly List<string> logLines = new List<string>();
    // 재대결(ResetGame)로 GameManager 인스턴스가 통째로 바뀌면 예전 bool 가드(subscribed)로는
    // "새 인스턴스에는 아직 구독 안 한 상태"를 구분 못 해서 재구독이 안 됐음 - 어떤 인스턴스에
    // 구독했는지 자체를 들고 있다가, SubscribeToGame()이 다시 불리면 바뀐 인스턴스인지 비교함
    private GameManager subscribedGame;

    // 네트워크가 정해준 "선공이 누구인지"(Me=내가 선공, Opponent=상대가 선공). 로그 표시용으로만
    // 씀 — "내 턴인지/내가 이겼는지" 같은 판정에는 절대 쓰면 안 됨(물리 슬롯이 항상 PlayerSide.Me
    // 쪽에 기록되는 것처럼, 그런 판정은 선공/후공과 무관하게 항상 PlayerSide.Me 기준이어야 하기
    // 때문 — OnEndTurnButtonClicked/ShowResult 참고)
    private GameNetworkManager subscribedNetwork;
    private System.Action<PlayerSide> networkStart;
    private System.Action networkTurn, networkCoin, networkDraw, networkDisconnected, networkRematch;
    private System.Action<int, int> networkSpell;
    private PlayerSide localSide = PlayerSide.Me;
    private GameManager pendingResultGame;
    private PlayerSide? pendingWinner;

    void Start()
    {
        SubscribeToGame();
        SubscribeToNetwork();

        if (logPanel != null) logPanel.SetActive(false); // 평소엔 숨겨두고 토글 버튼으로만 켬
        if (logToggleButton != null) logToggleButton.onClick.AddListener(ToggleLogPanel);
        if (resultPanel != null) resultPanel.SetActive(false); // 평소엔 숨겨두고 게임이 끝날 때만 켬

        UpdateCostHealthText();
        var rules = VRGameSession.Game.Rules;
        AddLog($"게임 시작: 체력 {rules.heroHealth}, 시작 손패 {rules.openingHand}장, 매 턴 {rules.cardsPerTurn}장 드로우");
    }

    void Update()
    {
        // 코스트/체력/상대 필드는 카드 내기 등 여러 경로에서 수시로 바뀌고 전용 변경 이벤트가
        // 따로 없어서, DemoTurnController(UpdateTurnText)와 동일하게 매 프레임 폴링해서 갱신함
        UpdateCostHealthText();
        UpdateOpponentFieldText();
        if (pendingResultGame != null && ProductionGameController.Current?.CombatPlaying != true)
        {
            if (pendingResultGame == VRGameSession.Game) ShowResult(pendingWinner);
            pendingResultGame = null;
        }
    }

    // 물리 "End Turn" 버튼의 OnClick()에 연결
    // [ContextMenu] 추가: End Turn 버튼이 테이블 위에 물리적으로 배치돼 있어서(VR/카메라 없이
    // 에디터 Game 뷰만으로는 화면에 안 잡히고 클릭도 안 됨) 테스트용으로, Play 모드에서
    // 이 컴포넌트(GameHUDController)를 Inspector에서 선택한 뒤 톱니바퀴(⋮) 아이콘이나
    // 컴포넌트 이름 우클릭 → "End Turn 테스트(디버그)"를 누르면 실제 버튼을 누른 것과
    // 똑같이 이 메서드가 호출됨. 기능 자체엔 영향 없고(기존 OnClick 연결 그대로 유지),
    // 테스트 편의용으로만 추가한 것이라 나중에 지워도 무방함
    [ContextMenu("End Turn 테스트(디버그)")]
    public void OnEndTurnButtonClicked()
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return;
        if (!VRGameSession.NetworkPlayAllowed) return;
        if (VRGameSession.Game.IsGameOver) return;

        var net = GameNetworkManager.Instance;
        if (net.Connected)
        {
            // 네트워크 연결 중엔 내 턴일 때만 턴을 넘기고, 넘길 때 상대에게도 알림
            // (내 턴이 아니면 TrySendEndTurn이 false를 돌려줘서 아래 EndTurn()을 안 부름).
            //
            // 여기서 반드시 PlayerSide.Me를 써야 함 — localSide는 "누가 선공인지"(네트워크가
            // 정해서 알려준 값)라서, 내가 후공으로 배정된 판에서는 Opponent가 들어있음. 예전엔
            // 그 값을 그대로 넘겨서, 후공으로 배정된 클라이언트는 자기 턴이 와도
            // currentTurn(Me) != localSide(Opponent)로 판정돼 End Turn이 영원히 막히는 버그가
            // 있었음. 물리 슬롯(SlotModelSpawner)이 항상 PlayerSide.Me 쪽에 기록되는 것과 마찬가지로,
            // "내 턴인지" 체크도 선공/후공과 무관하게 항상 PlayerSide.Me 기준이어야 함
            if (!net.TrySendEndTurn(PlayerSide.Me, VRGameSession.Game.State.CurrentPlayer)) return;
        }

        ProductionGameController.Current?.TableFeedback?.BindGame();
        VRGameSession.Game.EndTurn();
        UpdateCostHealthText();
    }

    [ContextMenu("후공 동전 사용(이번 턴 코스트 +1)")]
    public void OnCoinCardUsed()
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return;
        if (!VRGameSession.NetworkPlayAllowed) return;
        if (!VRGameSession.Coin.TryUse(PlayerSide.Me)) return;
        var net = GameNetworkManager.Instance;
        if (net.Connected) net.SendCoinUsed();
        AddLog("동전 사용: 이번 턴 코스트 +1");
        UpdateCostHealthText();
    }

    public bool CanUseCoin => VRGameSession.Coin.CanUse(PlayerSide.Me);

    public bool TryUseSpell(int cardId, int targetLane = -1)
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.CanAct) return false;
        if (!VRGameSession.NetworkPlayAllowed) return false;
        if (cardDatabase == null) return false;
        var card = cardDatabase.GetCardById(cardId);
        if (!VRGameSession.TryCastPhysicalSpell(card, PlayerSide.Me, targetLane)) return false;
        var net = GameNetworkManager.Instance;
        if (net.Connected) net.SendSpellCast(cardId, targetLane);
        UpdateCostHealthText();
        return true;
    }

    [ContextMenu("마법 테스트/지식의두루마리")]
    public void PreviewDrawSpell() => TryUseSpell(24);
    [ContextMenu("마법 테스트/수호의축복")]
    public void PreviewHealSpell() => TryUseSpell(25);
    [ContextMenu("마법 테스트/심판의낙뢰(양쪽 필드)")]
    public void PreviewGlobalSpell() => TryUseSpell(26);
    [ContextMenu("마법 테스트/전쟁의함성")]
    public void PreviewSweepSpell() => TryUseSpell(27);
    [ContextMenu("실물 카드 1장 뽑기 확인")]
    public void ConfirmDrawnCard()
    {
        if (VRGameSession.RequireNetwork && ProductionGameController.Current != null && !ProductionGameController.Current.HardwareReady) return;
        if (ProductionGameController.Current?.CombatPlaying == true) return;
        if (!VRGameSession.NetworkPlayAllowed) return;
        if (!VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me)) return;
        var net = GameNetworkManager.Instance;
        if (net.Connected) net.SendDrawConfirmed();
    }

    // 로그 토글 버튼의 OnClick()에 연결(코드에서 AddListener로 자동 연결됨, 인스펙터에서 또 연결할 필요 없음)
    public void ToggleLogPanel()
    {
        if (logPanel == null) return;
        logPanel.SetActive(!logPanel.activeSelf);
    }

    // 나중에 선공/후공 결정 연출을 붙일 때, 그 연출이 끝나는 시점에 호출해주면 로그에 남음
    // (지금은 VRGameSession.Game.StartGame()이 연출 없이 조용히 바로 선공을 정함)
    public void OnFirstPlayerDetermined(PlayerSide firstPlayer)
    {
        AddLog(firstPlayer == PlayerSide.Me ? "내가 선공" : "상대가 선공");
    }

    // 재대결 버튼(아직 물리 버튼은 없음 - End Turn처럼 나중에 테이블에 추가하면 됨)의 OnClick()에
    // 연결할 메서드. End Turn과 마찬가지로 [ContextMenu]로도 테스트 가능하게 해둠.
    // 누르는 쪽은 바로 로컬 보드를 리셋하고, 네트워크 연결 중이면 상대에게도 알림(상대 쪽은
    // OnRematchReceived로 똑같이 리셋됨) - 선공 재추첨은 항상 호스트가 담당함
    // (GameNetworkManager.ReflipFirstPlayerAndBroadcast 참고)
    [ContextMenu("재대결 테스트(디버그)")]
    public void OnRematchButtonClicked()
    {
        if (ProductionGameController.Current?.CombatPlaying == true) return;
        if (!VRGameSession.NetworkPlayAllowed) return;
        var net = GameNetworkManager.Instance;
        if (net.Connected) net.SendRematch();
        DoLocalRematch();
    }

    // 로컬 보드/UI를 깨끗한 새 게임으로 되돌림. 버튼을 직접 누른 쪽과, 상대가 눌러서
    // OnRematchReceived로 알려온 쪽 양쪽에서 똑같이 호출함
    private void DoLocalRematch()
    {
        VRGameSession.ResetGame();
        SubscribeToGame(); // 방금 새로 만들어진 GameManager 인스턴스에 다시 구독

        if (resultPanel != null) resultPanel.SetActive(false);
        UpdateCostHealthText();
        AddLog("재대결 시작");
    }

    private void SubscribeToGame()
    {
        GameManager game = VRGameSession.Game;
        if (subscribedGame == game) return; // 이미 이 인스턴스에 구독했으면 중복 구독 방지
        subscribedGame = game;

        game.State.OnTurnChanged += side => AddLog(side == PlayerSide.Me ? "내 턴 시작" : "상대 턴 시작");
        game.OnFaceHit += side => AddLog(side == PlayerSide.Me ? "내 명치를 맞음" : "상대 명치를 맞음");
        game.OnMinionAttacked += (side, lane) => AddLog($"{SideLabel(side)} {lane + 1}라인 공격");
        game.OnMinionHit += (side, lane) => AddLog($"{SideLabel(side)} {lane + 1}라인 피해");
        game.OnSpellCast += (side, card) => AddLog($"{SideLabel(side)} 마법 사용: {card.cardName}");
        game.OnDrawRequested += (side, count) =>
        {
            if (side == PlayerSide.Me) AddLog($"실물 카드 {count}장 드로우 요청");
        };
        game.OnGameOver += winner =>
        {
            AddLog(winner == null ? "무승부" : $"{SideLabel(winner.Value)} 승리");
            if (ProductionGameController.Current?.CombatPlaying == true)
            {
                pendingResultGame = game;
                pendingWinner = winner;
            }
            else ShowResult(winner);
        };
    }

    // 네트워크 이벤트 구독 - 연결이 전혀 안 된 솔로 플레이에서는 이 이벤트들이 그냥 안 울릴 뿐,
    // 기존 동작(OnEndTurnButtonClicked 등)엔 영향 없음
    private void SubscribeToNetwork()
    {
        var net = GameNetworkManager.Instance;
        net.ConfigureMatch(cardDatabase);
        subscribedNetwork = net;

        net.OnGameStartReceived += networkStart = side =>
        {
            // 코스트 공식(isSecondPlayer)이 네트워크가 정한 진짜 선공과 어긋나지 않도록
            // GameManager를 다시 올바른 선공으로 맞춤 (VRGameSession.ApplyNetworkFirstPlayer 참고)
            VRGameSession.ApplyNetworkFirstPlayer(side);
            SubscribeToGame();
            if (resultPanel != null) resultPanel.SetActive(false);
            localSide = side;
            AddLog(side == PlayerSide.Me ? "네트워크 연결 - 내가 선공" : "네트워크 연결 - 상대가 선공");
        };

        net.OnEndTurnReceived += networkTurn = () =>
        {
            // 상대가 이미 자기 턴에서 TrySendEndTurn을 통과한 뒤 보낸 메시지라
            // 여기서 다시 턴 체크 안 하고 그대로 반영함(lockstep)
            ProductionGameController.Current?.TableFeedback?.BindGame();
            if (!VRGameSession.Game.IsGameOver) VRGameSession.Game.EndTurn();
        };

        net.OnCardPlayedReceived += ApplyOpponentCardPlayed;
        net.OnSpellCastReceived += networkSpell = (id, lane) =>
        {
            if (cardDatabase != null && VRGameSession.TryCastPhysicalSpell(cardDatabase.GetCardById(id), PlayerSide.Opponent, lane))
                AddLog("상대 마법 사용");
        };
        net.OnDrawConfirmedReceived += networkDraw = () => VRGameSession.ConfirmPhysicalDraw(PlayerSide.Opponent);
        net.OnCoinUsedReceived += networkCoin = () =>
        {
            if (VRGameSession.Coin.TryUse(PlayerSide.Opponent)) AddLog("상대 동전 사용");
        };

        net.OnDisconnected += networkDisconnected = () =>
        {
            // 자동으로 재접속을 시도하긴 하지만(GameNetworkManager.RestartDiscoveryForReconnect),
            // 끊기기 전 보드 상태까지 다시 맞추는 건 아니라서 로그로만 알림 - 필요하면 재접속 후
            // 재대결 버튼을 눌러서 깨끗하게 다시 시작하면 됨
            AddLog("연결 끊김 - 재접속 시도 중");
        };

        net.OnRematchReceived += networkRematch = () =>
        {
            // 내가 버튼을 눌러서 온 경우(SendRematch 안에서 바로 호출됨)와, 상대가 눌러서
            // 네트워크로 온 경우 둘 다 여기로 들어옴 - DoLocalRematch는 몇 번 더 불려도
            // 안전함(그냥 또 깨끗한 새 게임으로 리셋될 뿐)
            DoLocalRematch();
        };
    }

    void OnDestroy()
    {
        if (logToggleButton != null) logToggleButton.onClick.RemoveListener(ToggleLogPanel);
        var net = subscribedNetwork;
        if (net == null) return;
        net.OnGameStartReceived -= networkStart;
        net.OnEndTurnReceived -= networkTurn;
        net.OnCoinUsedReceived -= networkCoin;
        net.OnDrawConfirmedReceived -= networkDraw;
        net.OnDisconnected -= networkDisconnected;
        net.OnRematchReceived -= networkRematch;
        net.OnSpellCastReceived -= networkSpell;
        net.OnCardPlayedReceived -= ApplyOpponentCardPlayed;
    }

    // 상대가 물리 카드를 냈다는 네트워크 메시지(SlotModelSpawner.TryPlayOnField가 보냄)를 받아서
    // 내 쪽 게임 로직(Opponent 보드)에도 똑같이 반영함. 상대는 이미 자기 턴/코스트 검증을 통과한
    // 뒤에 보낸 거라(위 OnEndTurnReceived와 같은 lockstep 원칙) 여기서 다시 검증하지 않고 그대로 적용함.
    // 상대 쪽은 아직 물리 슬롯이 없어서(SlotModelSpawner 주석 참고) 홀로그램은 안 띄우고 게임
    // 로직(코스트 차감/라인 배치/회복·앞의 적 피해 전투의함성)만 맞춤 — 비주얼은 상대 쪽 물리
    // 슬롯이 생기면 그때 추가하면 됨. 드로우/무기는 SlotModelSpawner도 VR에서는 아직 안 보내므로
    // (손패 없음 / 대상 선택 UI 없음) 여기서도 처리하지 않음.
    private void ApplyOpponentCardPlayed(int cardId, int laneIndex)
    {
        if (cardDatabase == null) return;

        CardData data = cardDatabase.GetCardById(cardId);
        if (data == null || data.cardType != CardType.Unit) return; // 마법 카드는 아직 안 보내짐

        GameManager game = VRGameSession.Game;
        if (game.IsGameOver) return;

        var board = game.Board.opponent;
        if (laneIndex < 0 || laneIndex >= board.lanes.Length) return;
        if (board.lanes[laneIndex] != null) return; // 이미 차 있으면 중복 메시지로 보고 무시
        if (VRGameSession.PhysicalHandCount(PlayerSide.Opponent) <= 0) return;

        var minion = new CardInstance(data, PlayerSide.Opponent);
        if (!board.TryPlaceOnLane(minion, laneIndex)) return;

        board.currentCost -= data.cost;
        VRGameSession.ConsumePhysicalCard(PlayerSide.Opponent);

        int amount = minion.currentAttack;
        if (data.HasKeyword(CardKeyword.Draw)) game.DrawCards(PlayerSide.Opponent, 1);
        if (data.HasKeyword(CardKeyword.Heal))
        {
            game.ApplyHealToLeftAlly(PlayerSide.Opponent, laneIndex, amount);
        }
        if (data.HasKeyword(CardKeyword.FrontDamage))
        {
            game.ApplyFrontDamage(PlayerSide.Opponent, laneIndex, amount);
        }
        ProductionGameController.Current?.ArmWeapon(PlayerSide.Opponent, data);
    }

    // 상대 필드(라인 4개)에 지금 뭐가 있는지 한 줄 텍스트로 보여줌. 상대 쪽은 아직 물리 슬롯이
    // 없어서 홀로그램을 못 띄우니(ApplyOpponentCardPlayed 참고), 최소한 "뭐가 있는지"는 알 수
    // 있게 HUD 텍스트로 대신 보여줌. 전용 변경 이벤트가 없어서 코스트/체력과 동일하게 매 프레임
    // 폴링함 — 카드가 전투로 죽는 것도(RemoveDeadCards) 네트워크 메시지 없이 여기서 자동으로
    // 반영됨(End Turn 메시지를 받으면 이 클라이언트에서도 똑같이 전투 페이즈가 로컬로 돌아가기 때문)
    private void UpdateOpponentFieldText()
    {
        if (opponentFieldText == null) return;

        var feedback = ProductionGameController.Current?.TableFeedback;
        var lanes = VRGameSession.Game.Board.opponent.lanes;
        var parts = new string[lanes.Length];
        for (int i = 0; i < lanes.Length; i++)
        {
            var card = feedback != null ? feedback.DisplayedCard(PlayerSide.Opponent, i) : lanes[i];
            if (card == null) { parts[i] = "-"; continue; }
            int attack = card.currentAttack, health = card.currentHealth;
            feedback?.DisplayedStats(PlayerSide.Opponent, i, card, out attack, out health);
            parts[i] = $"{card.data.cardName} {attack}/{health}";
        }
        opponentFieldText.text = "상대 필드  " + string.Join("   ", parts);
    }

    // 결과 화면 표시. 로그 패널(기본 숨김)과 달리 항상 바로 보이는 위치에 크게 띄움.
    // 승패 판정은 항상 PlayerSide.Me 기준(위 localSide 필드 설명 참고) — Me는 선공/후공과
    // 무관하게 항상 "나"(이 기기를 쓰는 사람)의 보드라서, winner == Me면 무조건 내 승리가 맞음
    private void ShowResult(PlayerSide? winner)
    {
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText == null) return;

        if (winner == null)
        {
            resultText.text = "무승부";
            resultText.color = Color.yellow;
        }
        else if (winner == PlayerSide.Me)
        {
            resultText.text = "승리!";
            resultText.color = Color.green;
        }
        else
        {
            resultText.text = "패배...";
            resultText.color = Color.red;
        }
    }

    private static string SideLabel(PlayerSide side) => side == PlayerSide.Me ? "나" : "상대";

    private void AddLog(string line)
    {
        logLines.Add(line);
        if (logLines.Count > maxLogLines) logLines.RemoveAt(0);

        if (logText != null) logText.text = string.Join("\n", logLines);
    }

    private void UpdateCostHealthText()
    {
        GameManager game = VRGameSession.Game;

        var feedback = ProductionGameController.Current?.TableFeedback;
        if (game.IsGameOver && feedback?.IsPlaying != true)
        {
            if (healthText != null) healthText.text = "체력\n게임 종료";
            return;
        }

        // 화면 왼쪽아래/오른쪽아래는 항상 "나"(헤드셋을 쓴 사람) 기준 — 상대 턴이어도 내 코스트/체력을 보여줌
        var myBoard = game.Board.me;

        if (costText != null)
        {
            costText.text = $"코스트\n{myBoard.currentCost} / {VRGameSession.Coin.DisplayCapacity(PlayerSide.Me)}";
        }

        if (healthText != null)
        {
            int remainingLives = feedback?.DisplayedHeroHealth(PlayerSide.Me) ?? myBoard.HeroHealth;
            healthText.text = $"체력\n{remainingLives} / {myBoard.maxHeroHealth}";
        }
    }
}

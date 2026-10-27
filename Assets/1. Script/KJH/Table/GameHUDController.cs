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

    // 명치 체력은 "맞은 횟수" 기준(GameRules.FaceHitThreshold = 10)이라, 화면엔 10에서 그 횟수를
    // 뺀 "남은 목숨"으로 보여줌(사용자 지시: "10에서 남은 뺸 목숨으로 바꿔")
    private const int StartingLives = GameRules.FaceHitThreshold;

    private readonly List<string> logLines = new List<string>();
    private bool subscribed;

    // 네트워크 플레이 중일 때 "나"가 어느 side로 배정됐는지 (연결 전/솔로 플레이는 항상 Me)
    private PlayerSide localSide = PlayerSide.Me;

    void Start()
    {
        SubscribeToGame();
        SubscribeToNetwork();

        if (logPanel != null) logPanel.SetActive(false); // 평소엔 숨겨두고 토글 버튼으로만 켬
        if (logToggleButton != null) logToggleButton.onClick.AddListener(ToggleLogPanel);

        UpdateCostHealthText();
        AddLog("게임 시작");
    }

    void Update()
    {
        // 코스트/체력은 카드 내기 등 여러 경로에서 수시로 바뀌고 전용 변경 이벤트가 따로 없어서,
        // DemoTurnController(UpdateTurnText)와 동일하게 매 프레임 폴링해서 갱신함
        UpdateCostHealthText();
    }

    // 물리 "End Turn" 버튼의 OnClick()에 연결
    public void OnEndTurnButtonClicked()
    {
        if (VRGameSession.Game.IsGameOver) return;

        var net = GameNetworkManager.Instance;
        if (net.Connected)
        {
            // 네트워크 연결 중엔 내 턴일 때만 턴을 넘기고, 넘길 때 상대에게도 알림
            // (내 턴이 아니면 TrySendEndTurn이 false를 돌려줘서 아래 EndTurn()을 안 부름)
            if (!net.TrySendEndTurn(localSide, VRGameSession.Game.State.CurrentPlayer)) return;
        }

        VRGameSession.Game.EndTurn();
        UpdateCostHealthText();
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

    private void SubscribeToGame()
    {
        if (subscribed) return;
        subscribed = true;

        GameManager game = VRGameSession.Game;
        game.State.OnTurnChanged += side => AddLog(side == PlayerSide.Me ? "내 턴 시작" : "상대 턴 시작");
        game.OnFaceHit += side => AddLog(side == PlayerSide.Me ? "내 명치를 맞음" : "상대 명치를 맞음");
        game.OnMinionAttacked += (side, lane) => AddLog($"{SideLabel(side)} {lane + 1}라인 공격");
        game.OnMinionHit += (side, lane) => AddLog($"{SideLabel(side)} {lane + 1}라인 피해");
        game.OnGameOver += winner => AddLog(winner == null ? "무승부" : $"{SideLabel(winner.Value)} 승리");
    }

    // 네트워크 이벤트 구독 - 연결이 전혀 안 된 솔로 플레이에서는 이 이벤트들이 그냥 안 울릴 뿐,
    // 기존 동작(OnEndTurnButtonClicked 등)엔 영향 없음
    private void SubscribeToNetwork()
    {
        var net = GameNetworkManager.Instance;

        net.OnGameStartReceived += side =>
        {
            // 코스트 공식(isSecondPlayer)이 네트워크가 정한 진짜 선공과 어긋나지 않도록
            // GameManager를 다시 올바른 선공으로 맞춤 (VRGameSession.ApplyNetworkFirstPlayer 참고)
            VRGameSession.ApplyNetworkFirstPlayer(side);
            localSide = side;
            AddLog(side == PlayerSide.Me ? "네트워크 연결 - 내가 선공" : "네트워크 연결 - 상대가 선공");
        };

        net.OnEndTurnReceived += () =>
        {
            // 상대가 이미 자기 턴에서 TrySendEndTurn을 통과한 뒤 보낸 메시지라
            // 여기서 다시 턴 체크 안 하고 그대로 반영함(lockstep)
            if (!VRGameSession.Game.IsGameOver) VRGameSession.Game.EndTurn();
        };
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

        if (game.IsGameOver)
        {
            if (healthText != null) healthText.text = "체력\n게임 종료";
            return;
        }

        // 화면 왼쪽아래/오른쪽아래는 항상 "나"(헤드셋을 쓴 사람) 기준 — 상대 턴이어도 내 코스트/체력을 보여줌
        var myBoard = game.Board.me;

        if (costText != null)
        {
            costText.text = $"코스트\n{myBoard.currentCost} / {myBoard.maxCost}";
        }

        if (healthText != null)
        {
            int remainingLives = Mathf.Max(0, StartingLives - myBoard.faceHitCount);
            healthText.text = $"체력\n{remainingLives} / {StartingLives}";
        }
    }
}

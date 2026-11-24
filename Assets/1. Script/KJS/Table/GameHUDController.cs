using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 화면 표시와 버튼 입력만 담당한다. 게임 동기화는 MultiplayerManager가 처리한다.
public class GameHUDController : MonoBehaviour
{
    [Header("코스트 / 체력 표시 (왼쪽 아래 / 오른쪽 아래)")]
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI healthText;
    [Header("디버그 캔버스")]
    [SerializeField] private TextMeshProUGUI debugTurn;
    [SerializeField] private TextMeshProUGUI debugHealth;
    [SerializeField] private TextMeshProUGUI debugMana;

    [Header("로그 창 (오른쪽 위 토글)")]
    [SerializeField] private GameObject logPanel;
    [SerializeField] private TextMeshProUGUI logText;
    [SerializeField] private Button logToggleButton;
    [Tooltip("로그에 쌓아둘 최대 줄 수 — 넘으면 오래된 줄부터 지움")]
    [SerializeField] private int maxLogLines = 30;

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

    private MultiplayerManager multiplayer;
    private GameManager pendingResultGame;
    private PlayerSide? pendingWinner;

    void Start()
    {
        multiplayer = MultiplayerManager.GetOrCreate(cardDatabase);
        multiplayer.OnLog += AddLog;
        VRGameSession.PhysicalFatigueApplied += LogFatigue;
        SubscribeToGame();

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
        if (subscribedGame != VRGameSession.Game)
        {
            SubscribeToGame();
            if (resultPanel != null) resultPanel.SetActive(false);
        }
        UpdateCostHealthText();
        UpdateDebugText();
        UpdateOpponentFieldText();
        if (pendingResultGame != null && ProductionGameController.Current?.CombatPlaying != true)
        {
            if (pendingResultGame == VRGameSession.Game) ShowResult(pendingWinner);
            pendingResultGame = null;
        }
    }

    public void OnEndTurnButtonClicked() => multiplayer?.EndTurn();
    public void OnCoinCardUsed() => multiplayer?.UseCoin();
    public bool CanUseCoin => VRGameSession.Coin.CanUse(PlayerSide.Me);
    public bool TryUseSpell(int cardId, int targetLane = -1) => multiplayer != null && multiplayer.TryUseSpell(cardId, targetLane);
    public void PreviewDrawSpell() => TryUseSpell(24);
    public void PreviewHealSpell() => TryUseSpell(25);
    public void PreviewGlobalSpell() => TryUseSpell(26);
    public void PreviewSweepSpell() => TryUseSpell(27);
    public void ConfirmDrawnCard() => multiplayer?.ConfirmDraw();

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

    public void OnRematchButtonClicked() => multiplayer?.Rematch();

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

    void OnDestroy()
    {
        if (logToggleButton != null) logToggleButton.onClick.RemoveListener(ToggleLogPanel);
        if (multiplayer != null) multiplayer.OnLog -= AddLog;
        VRGameSession.PhysicalFatigueApplied -= LogFatigue;
    }

    private void LogFatigue(PlayerSide side, int damage) =>
        AddLog($"{SideLabel(side)} · 덱 소진 · 탈진 피해 {damage}");

    private void UpdateOpponentFieldText()
    {
        if (opponentFieldText == null) return;

        var session = ProductionGameController.Current;
        if (session != null && (!session.Ready || !session.HardwareReady))
        {
            opponentFieldText.text = session.ReadinessHint;
            return;
        }

        var feedback = session?.TableFeedback;
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

    private void UpdateDebugText()
    {
        var game = VRGameSession.Game;
        var session = ProductionGameController.Current;
        if (debugTurn != null)
            debugTurn.text = game.IsGameOver ? "게임 종료" :
                session != null && !session.Ready ? "준비 중" :
                game.State.CurrentPlayer == PlayerSide.Me ? "내 턴" : "상대 턴";
        if (debugHealth != null)
            debugHealth.text = $"체력: {session?.TableFeedback?.DisplayedHeroHealth(PlayerSide.Me) ?? game.Board.me.HeroHealth}";
        if (debugMana != null)
            debugMana.text = $"마나: {game.Board.me.currentCost} / {VRGameSession.Coin.DisplayCapacity(PlayerSide.Me)}";
    }
}

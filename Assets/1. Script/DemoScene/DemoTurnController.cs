using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// KJH_CardTest 씬 전용 — 턴 구조/승패/코스트/필드 로직(GameManager)을 UI와 이어붙이는 데모 컨트롤러.
///
/// 필요한 능력(Battlecry, Cleave 등)은 이번 데모에서 전부 뺐음 — 나중에 따로 붙일 예정.
/// 데모 규칙: 필드에 나온 하수인은 실제 카드 스탯과 무관하게 전부 공격력 1 / 체력 2로 고정 (TryPlaceMinion 참고)
/// </summary>
public class DemoTurnController : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("테스트용 덱 (선택사항)")]
    [Tooltip("비워두면 드로우가 계속 실패하고, 시작하자마자 '둘 다 카드 없음' 상태라 바로 무승부로 끝남. " +
             "턴/코스트 흐름을 여러 턴 지켜보고 싶으면 CardData 에셋을 몇 장 넣어두는 걸 추천")]
    [SerializeField] private List<CardData> testDeckMe = new List<CardData>();
    [SerializeField] private List<CardData> testDeckOpponent = new List<CardData>();

    [Tooltip("턴 종료 시 반대편을 보도록 카메라를 돌려주는 컨트롤러 (Main Camera에 붙임)")]
    [SerializeField] private CameraTurnController cameraTurnController;

    [Header("필드 슬롯 (My/EnemyFieldPosition 하위 8개 전부)")]
    [Tooltip("카드를 냈을 때 실제로 어느 라인에 하수인이 들어가는지 판단하는 데 씀. " +
             "MyFieldPosition/EnemyFieldPosition 하위 FieldPosition 오브젝트 8개를 전부 넣어두면 됨")]
    [SerializeField] private List<FieldSlot> fieldSlots = new List<FieldSlot>();

    [Header("드로우 (MyDeck / EnemyFieldPosition 하위 EnemyDeck)")]
    [Tooltip("Draw 버튼을 누르면 지금 턴 플레이어가 Me인지 Opponent인지에 따라 " +
             "이 둘 중 하나의 DrawManager만 실제로 카드를 뽑음")]
    [SerializeField] private DrawManager myDrawManager;
    [SerializeField] private DrawManager enemyDrawManager;

    private GameManager gameManager;

    void Start()
    {
        gameManager = new GameManager();
        gameManager.OnGameOver += HandleGameOver;

        // StartGame()이 첫 턴 드로우까지 바로 처리하므로, 덱은 그 전에 채워둠
        gameManager.Board.me.deck.AddRange(testDeckMe);
        gameManager.Board.opponent.deck.AddRange(testDeckOpponent);

        gameManager.StartGame();

        // 선공은 StartGame() 안에서 랜덤으로 정해지는데, Main Camera 기본 배치는 항상
        // "Me가 선공"이라고 가정한 방향임. 실제 선공이 Opponent로 뽑혔으면 카메라를 한 번
        // 즉시(애니메이션 없이) 맞춰줘야, 1턴부터 매 턴 카메라가 계속 반대로 보이는 문제가 안 생김
        if (gameManager.State.CurrentPlayer == PlayerSide.Opponent && cameraTurnController != null)
        {
            cameraTurnController.SnapFlip();
        }

        if (resultText != null) resultText.text = "";
        UpdateTurnText();
    }

    // "턴 종료" 버튼 OnClick에 연결. 이 데모에서는 누르는 즉시 상대 턴(Main 페이즈)으로 넘어감.
    // 이때 GameManager가 전투 페이즈까지 알아서 진행시키므로, 필드에 실제로 놓인 하수인끼리
    // (혹은 명치로) 자동으로 한 번 공격이 일어남 — 죽은 하수인의 비주얼은 SyncDeadMinions로 같이 치움
    public void OnEndTurnButtonClicked()
    {
        if (gameManager.IsGameOver) return;

        gameManager.EndTurn();
        UpdateTurnText();
        SyncDeadMinions();

        // 드로우는 이제 Draw 버튼을 직접 눌렀을 때만 일어남 (턴 종료 시 자동 드로우 없음)
        if (cameraTurnController != null) cameraTurnController.FlipCamera();
    }

    // "드로우" 버튼 OnClick에 연결. 지금 턴 플레이어(Me/Opponent)의 실제 덱에서 카드를 한 장
    // 손패로 옮기고, 성공했을 때만 그 편의 DrawManager로 비주얼 카드를 그 편 손패 자리에 띄움.
    // 예전에는 DrawManager.OnDrawButtonClick()을 버튼에 직접 연결해서 항상 같은 쪽(내 덱)에서만
    // 카드가 나왔고, 실제 손패 데이터와도 연결되어 있지 않았음 — 그래서 턴이 넘어가도 항상 내 쪽에서
    // 카드가 나오고, 코스트가 남아 있어도 실제 손패(GameManager 쪽)에는 카드가 없어서 못 내는 버그가 있었음
    public void OnDrawButtonClicked()
    {
        if (gameManager == null || gameManager.IsGameOver) return;

        PlayerSide current = gameManager.State.CurrentPlayer;
        if (!gameManager.TryDrawCard(current)) return; // 덱이 비었으면 아무 일도 안 일어남

        DrawManager drawManager = current == PlayerSide.Me ? myDrawManager : enemyDrawManager;
        if (drawManager != null) drawManager.OnDrawButtonClick();

        UpdateTurnText();
    }

    // CardMove가 카드를 필드 슬롯 위에 내려놓을 때 호출.
    // 지금 턴 플레이어의 슬롯이 맞고, 그 라인이 비어있고, 코스트가 1 이상 있고, 낼 손패가 있어야 성공함.
    // 성공하면 코스트 1 소모 + 손패에서 한 장 빼서(카드 종류 구분은 안 함) 공1/체2 하수인으로 필드에 등록
    public bool TryPlaceMinion(FieldSlot slot, GameObject cardVisual)
    {
        if (gameManager == null || gameManager.IsGameOver || slot == null)
        {
            return false;
        }
        if (slot.Side != gameManager.State.CurrentPlayer)
        {
            return false;
        }

        var board = gameManager.Board.GetBoard(gameManager.State.CurrentPlayer);

        if (board.lanes[slot.LaneIndex] != null)
        {
            return false;
        }
        if (board.currentCost < 1)
        {
            return false;
        }
        if (board.hand.Count == 0)
        {
            return false;
        }

        CardData playedCard = board.hand[0];
        board.hand.RemoveAt(0);

        // 데모 규칙: 하수인은 원본 카드 스탯과 무관하게 전부 공격력 1 / 체력 2로 고정
        var minion = new CardInstance(playedCard, gameManager.State.CurrentPlayer)
        {
            currentAttack = 1,
            currentHealth = 2,
        };

        if (!board.TryPlaceOnLane(minion, slot.LaneIndex))
        {
            board.hand.Insert(0, playedCard); // 혹시 몰라 실패 시 손패 원복
            return false;
        }

        board.currentCost -= 1;
        slot.occupyingCard = cardVisual;

        // 손패에서 카드가 하나 빠졌으니, 그 편의 DrawManager한테 알려서 남은 손패 카드들이
        // 빈칸 없이 왼쪽으로 당겨지도록 함
        DrawManager drawManager = gameManager.State.CurrentPlayer == PlayerSide.Me ? myDrawManager : enemyDrawManager;
        if (drawManager != null) drawManager.RemoveCardFromHand(cardVisual);

        UpdateTurnText();
        return true;
    }

    // 전투 페이즈가 끝난 뒤(EndTurn 직후) 호출. 데이터상 죽어서 lanes[i]가 null이 됐는데
    // 아직 비주얼 카드가 필드에 남아있는 슬롯을 찾아서 그 오브젝트를 파괴함
    private void SyncDeadMinions()
    {
        foreach (var slot in fieldSlots)
        {
            if (slot == null || slot.occupyingCard == null) continue;

            var board = gameManager.Board.GetBoard(slot.Side);
            bool stillAlive = board.lanes[slot.LaneIndex] != null;

            if (!stillAlive)
            {
                Destroy(slot.occupyingCard);
                slot.occupyingCard = null;
            }
        }
    }

    private void UpdateTurnText()
    {
        if (turnText == null) return;

        // 참고: 프로젝트의 기본 TMP 폰트(Liberation Sans SDF)가 한글 글리프를 지원하지 않아서
        // 화면에 한글을 그대로 쓰면 글자가 깨져 보임 (한글 TMP 폰트 에셋 추가 전까지는 영문으로 표시)
        var board = gameManager.Board.GetBoard(gameManager.State.CurrentPlayer);
        string playerName = gameManager.State.CurrentPlayer == PlayerSide.Me ? "Me" : "Opponent";

        turnText.text =
            $"Turn {gameManager.TurnNumber} — {playerName}'s turn\n" +
            $"Face hits (Me {gameManager.Board.me.faceHitCount} : Opp {gameManager.Board.opponent.faceHitCount}) / {GameRules.FaceHitThreshold}";

        // 코스트는 따로 화면 왼쪽 아래에 표시
        if (costText != null)
        {
            costText.text = $"Cost {board.currentCost} / {board.maxCost}";
        }
    }

    private void HandleGameOver(PlayerSide? winner)
    {
        if (resultText == null) return;

        resultText.text = winner == null
            ? "Draw (both empty, tied face hits)"
            : $"{(winner == PlayerSide.Me ? "Me" : "Opponent")} wins!";
    }
}

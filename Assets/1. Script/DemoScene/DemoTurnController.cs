using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>
/// KJH_CardTest 씬 전용 — 턴 구조/승패/코스트/필드 로직(GameManager)을 UI와 이어붙이는 데모 컨트롤러.
///
/// 필드에 나온 하수인은 CardData의 실제 공격력/체력을 그대로 씀(TryPlayCard 참고) —
/// 예전엔 전부 공격력1/체력2로 고정해뒀었는데, 회복/무기/앞의 적 피해 같은 키워드가 전부
/// "카드 자신의 공격력만큼" 효과가 발동하는 구조라 고정값으로는 의미가 없어져서 실제 스탯을 쓰도록 바꿈.
///
/// 최종 카드 디자인(공용9+진영별7+마법4=27종)에 쓰이는 5개 유닛 키워드(드로우/반격/회복/무기/앞의 적
/// 피해)와 4개 마법 키워드(드로우/회복/제거/강화)는 전부 여기서 실행됨 — 유닛은 ResolveBattlecry,
/// 마법은 ResolveSpellEffect. 반격만 예외로, "공격받을 때" 발동하는 효과라 여기가 아니라
/// GameManager.HandleCombat(전투 처리) 쪽에서 처리함. 옛 설계에 있던 Cleave/SelfDestruct/Battlecry
/// 플래그는 최종 카드 디자인에서 빠져서 실행 로직이 없음(카드에도 안 붙어 있음).
/// </summary>
public class DemoTurnController : MonoBehaviour
{
    [Header("UI 연결")]
    [SerializeField] private TextMeshProUGUI turnText;
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("카드 덱 구성 (실제 게임과 동일한 로직)")]
    [Tooltip("27종 카드 전체가 들어있는 CardDatabase 에셋. 꽂혀 있으면 공용9+선택한 진영7+마법4=20장을 " +
             "매 게임 새로 랜덤 셔플해서 진짜 카드게임처럼 덱을 구성함. 이 로직(CardDatabase.BuildShuffledDeck)은 " +
             "GameLogic 폴더 소속이라 나중에 실제 게임 씬을 만들 때도 그대로 옮겨서 재사용하면 됨 — 데모 전용 코드가 아님")]
    [SerializeField] private CardDatabase cardDatabase;
    [SerializeField] private CardDatabase.Faction myFaction = CardDatabase.Faction.Human;
    [SerializeField] private CardDatabase.Faction opponentFaction = CardDatabase.Faction.Demon;

    [Header("테스트용 덱 (선택사항 — cardDatabase가 비어있을 때만 사용됨)")]
    [Tooltip("cardDatabase를 안 꽂아두면 이 리스트를 그대로 씀(카드 몇 장만 놓고 좁게 테스트하고 싶을 때 대비). " +
             "비워두면 드로우가 계속 실패하고, 시작하자마자 '둘 다 카드 없음' 상태라 바로 무승부로 끝남")]
    [SerializeField] private List<CardData> testDeckMe = new List<CardData>();
    [SerializeField] private List<CardData> testDeckOpponent = new List<CardData>();

    [Tooltip("턴 종료 시 반대편을 보도록 카메라를 돌려주는 컨트롤러 (Main Camera에 붙임)")]
    [SerializeField] private CameraTurnController cameraTurnController;

    [Header("필드 슬롯 (My/EnemyFieldPosition 하위 10개 전부: 라인 4x2 + 마법 전용 슬롯 1x2)")]
    [Tooltip("카드를 냈을 때 실제로 어느 라인에 하수인이 들어가는지 판단하는 데 씀. " +
             "MyFieldPosition/EnemyFieldPosition 하위 FieldPosition 오브젝트 전부(라인 슬롯 8개 + 마법 전용 슬롯 2개, " +
             "isSpellSlot=true)를 넣어두면 됨")]
    [SerializeField] private List<FieldSlot> fieldSlots = new List<FieldSlot>();

    [Header("드로우 (MyDeck / EnemyFieldPosition 하위 EnemyDeck)")]
    [Tooltip("Draw 버튼을 누르면 지금 턴 플레이어가 Me인지 Opponent인지에 따라 " +
             "이 둘 중 하나의 DrawManager만 실제로 카드를 뽑음")]
    [SerializeField] private DrawManager myDrawManager;
    [SerializeField] private DrawManager enemyDrawManager;

    [Header("무기 키워드 대상 선택")]
    [Tooltip("IFieldTargetPicker를 구현한 컴포넌트를 넣어야 함(지금은 MouseFieldTargetPicker). " +
             "나중에 VR용 구현체로 교체할 때 여기만 바꿔 끼우면 됨. 비워두면 무기 효과는 그냥 무시됨")]
    [SerializeField] private MonoBehaviour targetPickerBehaviour;
    private IFieldTargetPicker TargetPicker => targetPickerBehaviour as IFieldTargetPicker;

    private GameManager gameManager;

    void Start()
    {
        gameManager = new GameManager();
        gameManager.OnGameOver += HandleGameOver;

        // StartGame()이 첫 턴 드로우까지 바로 처리하므로, 덱은 그 전에 채워둠.
        // cardDatabase가 꽂혀 있으면 실제 게임과 완전히 같은 방식(공용9 + 선택한 진영7 + 마법4 = 20장,
        // 매번 랜덤 셔플)으로 덱을 구성하고, 안 꽂혀 있으면 예전처럼 testDeckMe/testDeckOpponent를
        // 그대로 씀(카드 몇 장만 놓고 좁게 테스트하고 싶을 때 대비한 하위 호환)
        if (cardDatabase != null)
        {
            gameManager.Board.me.deck.AddRange(cardDatabase.BuildShuffledDeck(myFaction));
            gameManager.Board.opponent.deck.AddRange(cardDatabase.BuildShuffledDeck(opponentFaction));
        }
        else
        {
            gameManager.Board.me.deck.AddRange(testDeckMe);
            gameManager.Board.opponent.deck.AddRange(testDeckOpponent);
        }

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

        PerformDraw(gameManager.State.CurrentPlayer);

        UpdateTurnText();
    }

    // 실제로 덱에서 카드를 한 장 뽑아 손패에 넣고, 성공하면 그 편 DrawManager로 비주얼까지 띄워주는
    // 공통 헬퍼. Draw 버튼(OnDrawButtonClicked)/유닛의 드로우 키워드(ResolveBattlecry)/마법의 드로우
    // 키워드(ResolveSpellEffect) 세 군데에서 전부 이 메서드를 씀 — 예전엔 세 곳에서 각자
    // TryDrawCard + DrawManager.OnDrawButtonClick()을 따로 호출했는데, DrawManager가 이제
    // 실제로 뽑힌 CardData를 받아서 카드 비주얼에 값을 채워야 하므로(CardView 참고) "방금 뽑힌
    // 카드가 뭔지" 알아내는 로직(hand 맨 뒤 카드 읽기)을 한 곳으로 모음
    private bool PerformDraw(PlayerSide side)
    {
        if (!gameManager.TryDrawCard(side)) return false; // 덱이 비었거나 손패가 꽉 찼으면 아무 일도 안 일어남

        var board = gameManager.Board.GetBoard(side);
        CardData drawnCard = board.hand[board.hand.Count - 1];

        DrawManager drawManager = side == PlayerSide.Me ? myDrawManager : enemyDrawManager;
        if (drawManager != null) drawManager.OnDrawButtonClick(drawnCard);

        return true;
    }

    // CardMove가 카드를 필드 슬롯 위에 내려놓을 때 호출.
    // 지금 턴 플레이어의 슬롯이 맞고, "실제로 드래그한 그 카드"의 코스트만큼 자원이 남아 있어야 성공함.
    //
    // 예전엔 무조건 board.hand[0](손패 맨 앞 카드)만 냈었음 — 그런데 CardMove는 마우스로 실제로 집은
    // 카드 비주얼(cardVisual)을 그대로 넘겨주므로, 손패 순서와 무관하게 "그 비주얼이 어떤 CardData인지"
    // (CardView.Data)로 실제로 낼 카드를 찾아야 함. 안 그러면 손패 2번째 이후 카드를 아무리 집어서
    // 내려놔도 코스트 체크는 항상 hand[0] 기준으로만 이뤄져서, hand[0]이 지금 코스트로 못 내는 비싼
    // 카드일 때 그 뒤에 있는 싸고 멀쩡한 카드조차 전혀 낼 수 없는 버그가 있었음(테스트 덱에서 상대
    // 쪽 첫 카드가 코스트 5짜리라 "상대 턴에는 카드 제출이 아예 안 된다"처럼 보였던 원인이 이거였음).
    //
    // 그 카드가 유닛인지 마법인지에 따라 갈라짐:
    //  - 유닛: 4라인 슬롯(빨간색, isSpellSlot=false)에만 낼 수 있고, 클릭한 슬롯의 라인이 비어있어야 하며,
    //    실제 스탯 그대로 하수인으로 등록 + 전투의함성 처리
    //  - 마법: 마법 전용 슬롯(파란색, isSpellSlot=true)에만 낼 수 있음. 라인 점유 개념은 없고
    //    (PlayerBoardState.spellSlot 필드 자체도 여전히 안 씀 — 마법 슬롯은 순수 UX용 드롭 위치일 뿐)
    //    그냥 즉시 효과만 발동하고 비주얼 카드는 소모되어 사라짐
    // 이름은 TryPlaceMinion이었는데 마법도 처리하게 되면서 TryPlayCard로 바꿈(CardMove.cs도 같이 수정)
    public bool TryPlayCard(FieldSlot slot, GameObject cardVisual)
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

        // 드래그한 비주얼이 실제로 어떤 CardData인지 CardView에서 읽어옴. 카드를 뽑을 때
        // DrawManager가 CardView.SetCardData로 채워주므로, 손패에 있는 카드 비주얼이라면
        // 항상 Data가 채워져 있어야 정상임
        var cardView = cardVisual != null ? cardVisual.GetComponent<CardView>() : null;
        CardData playedCard = cardView != null ? cardView.Data : null;

        if (playedCard == null)
        {
            return false;
        }

        // 그 카드가 실제로 지금 턴 플레이어의 손패 안에 있는지 확인 + 나중에 정확히 그 자리를
        // 빼기 위해 인덱스를 구함 (더 이상 무조건 0번이 아님)
        int handIndex = board.hand.IndexOf(playedCard);
        if (handIndex < 0)
        {
            return false; // 이미 낸 카드거나(중복 클릭 등) 손패 데이터와 비주얼이 어긋난 경우
        }

        // 예전엔 카드 종류 상관없이 코스트를 무조건 1만 썼는데, 실제 27종 카드 디자인은 코스트가
        // 1~5로 다양해서 그대로 두면 밸런스가 의미 없어짐 — 카드 자신의 cost를 그대로 씀
        if (board.currentCost < playedCard.cost)
        {
            return false;
        }

        // 마법 카드는 전용 슬롯(파란색)에만, 유닛 카드는 4라인 슬롯(빨간색)에만 낼 수 있음.
        // 마법 카드를 라인에 내거나 유닛을 마법 슬롯에 내는 건 둘 다 막아야 함
        bool wantsSpellSlot = playedCard.cardType == CardType.Spell;
        if (slot.IsSpellSlot != wantsSpellSlot)
        {
            return false;
        }

        if (playedCard.cardType == CardType.Spell)
        {
            return TryCastSpell(board, playedCard, handIndex, slot.Side, cardVisual);
        }

        return TryPlaceMinionOnLane(board, playedCard, handIndex, slot, cardVisual);
    }

    // 유닛 카드를 slot의 라인에 실제로 배치. 그 라인이 비어있어야 함
    private bool TryPlaceMinionOnLane(PlayerBoardState board, CardData playedCard, int handIndex, FieldSlot slot, GameObject cardVisual)
    {
        if (board.lanes[slot.LaneIndex] != null)
        {
            return false;
        }

        board.hand.RemoveAt(handIndex);

        // CardInstance 생성자가 이미 data.attack/data.health로 currentAttack/currentHealth를
        // 초기화해주므로 여기서 따로 값을 덮어쓸 필요 없음(예전엔 공격력1/체력2로 고정했었음)
        var minion = new CardInstance(playedCard, gameManager.State.CurrentPlayer);

        if (!board.TryPlaceOnLane(minion, slot.LaneIndex))
        {
            board.hand.Insert(handIndex, playedCard); // 혹시 몰라 실패 시 손패 원복(원래 자리 그대로)
            return false;
        }

        board.currentCost -= playedCard.cost;
        slot.occupyingCard = cardVisual;

        NotifyHandCardRemoved(cardVisual);
        ResolveBattlecry(playedCard, minion, slot);

        UpdateTurnText();
        return true;
    }

    // 마법 카드를 시전. 라인 개념이 없어서 바로 손패에서 빼고 코스트를 쓴 뒤 효과를 발동함.
    // 비주얼 카드는 필드에 남지 않고(유닛과 달리 slot.occupyingCard에 안 넣음) 그대로 파괴됨
    private bool TryCastSpell(PlayerBoardState board, CardData playedCard, int handIndex, PlayerSide side, GameObject cardVisual)
    {
        board.hand.RemoveAt(handIndex);
        board.currentCost -= playedCard.cost;

        NotifyHandCardRemoved(cardVisual);
        if (cardVisual != null) Destroy(cardVisual);

        ResolveSpellEffect(playedCard, side);

        UpdateTurnText();
        return true;
    }

    // 손패에서 카드가 하나 빠졌으니, 그 편의 DrawManager한테 알려서 남은 손패 카드들이
    // 빈칸 없이 왼쪽으로 당겨지도록 함 (유닛/마법 공통)
    private void NotifyHandCardRemoved(GameObject cardVisual)
    {
        DrawManager drawManager = gameManager.State.CurrentPlayer == PlayerSide.Me ? myDrawManager : enemyDrawManager;
        if (drawManager != null) drawManager.RemoveCardFromHand(cardVisual);
    }

    // 유닛 카드를 낼 때(전투의함성) 즉시 발동하는 키워드 효과 처리.
    // 드로우/회복/앞의 적 피해는 대상이 고정(본인 덱 / 왼쪽 아군 / 정면 라인)이라 바로 자동으로
    // 처리하고, 무기만 "플레이어가 직접 대상을 고르는" 효과라서 targetPicker한테 맡기고 콜백으로
    // 나중에 처리함 (VR로 넘어가도 이 메서드는 그대로 두고 targetPickerBehaviour 구현체만 바꾸면 됨)
    private void ResolveBattlecry(CardData playedCard, CardInstance minion, FieldSlot slot)
    {
        int amount = minion.currentAttack;

        if (playedCard.HasKeyword(CardKeyword.Draw))
        {
            PerformDraw(slot.Side);
            // 손패가 이미 꽉 찼으면(GameRules.MaxHandSize) PerformDraw 안의 TryDrawCard가 false를
            // 돌려주고, 그냥 아무 일도 안 일어남(카드가 사라지지 않고 덱에 남음) — 디자인 문서의 손패 규칙 그대로
        }

        if (playedCard.HasKeyword(CardKeyword.Heal))
        {
            gameManager.ApplyHealToLeftAlly(slot.Side, slot.LaneIndex, amount);
        }

        if (playedCard.HasKeyword(CardKeyword.FrontDamage))
        {
            gameManager.ApplyFrontDamage(slot.Side, slot.LaneIndex, amount);
            SyncDeadMinions();
        }

        if (playedCard.HasKeyword(CardKeyword.Weapon))
        {
            if (TargetPicker == null) return; // 대상 선택 창구가 안 꽂혀 있으면 그냥 무기 효과는 무시됨

            // !s.IsSpellSlot을 lanes[s.LaneIndex] 접근보다 먼저 체크해야 함 — 마법 전용 슬롯은
            // board.lanes(4칸)에 대응하는 라인이 없어서(laneIndex가 -1이거나 무의미함) 그대로 인덱싱하면
            // 범위 밖 접근으로 터짐. && 단락 평가 덕분에 이 슬롯은 아예 lanes[] 조회까지 안 감
            var validTargets = fieldSlots
                .Where(s => s != null && !s.IsSpellSlot && s.Side != slot.Side
                            && gameManager.Board.GetBoard(s.Side).lanes[s.LaneIndex] != null)
                .ToList();

            TargetPicker.RequestTarget(validTargets, chosen =>
            {
                if (chosen == null) return; // 취소되거나 유효 대상이 없었음

                gameManager.ApplyDamageToLane(chosen.Side, chosen.LaneIndex, amount);
                SyncDeadMinions();
            });
        }
    }

    // 마법 카드 시전 즉시 발동하는 효과 처리. 유닛의 ResolveBattlecry와 같은 자리인데, 마법은
    // 자기 자신의 공격력이 없어서(카드 자체가 attack=0) 대미지/버프 수치는 전부 GameRules 상수를 씀.
    //
    // 드로우는 유닛 쪽과 완전히 같은 효과(카드 1장 뽑기)라 그대로 재사용하고, 회복은 이름만 같고
    // 뜻이 다름(유닛=왼쪽 아군 체력 회복 / 마법=명치 맞은 횟수 1 감소) — 여기서는 마법 전용 뜻으로 처리함.
    // 제거는 무기와 똑같이 targetPicker로 적 하수인 하나를 고르되 대미지는 고정값(카드 자신의
    // 공격력이 없으므로). 강화는 대상 선택 없이 바로 자기 필드 전체에 적용됨
    private void ResolveSpellEffect(CardData playedCard, PlayerSide side)
    {
        if (playedCard.HasKeyword(CardKeyword.Draw))
        {
            PerformDraw(side);
        }

        if (playedCard.HasKeyword(CardKeyword.Heal)) // 마법의 "회복" = 명치가 맞은 횟수를 1 줄임
        {
            gameManager.ReduceFaceHitCount(side, 1);
            UpdateTurnText(); // 명치 표시가 UpdateTurnText 쪽에 있어서 바로 화면에 반영되도록
        }

        if (playedCard.HasKeyword(CardKeyword.Remove))
        {
            if (TargetPicker == null) return; // 무기와 동일하게, 대상 선택 창구가 없으면 그냥 무시됨

            // 무기 키워드 타겟팅과 동일한 이유로 !s.IsSpellSlot을 먼저 체크함(마법 슬롯은 lanes[] 인덱싱 대상이 아님)
            var validTargets = fieldSlots
                .Where(s => s != null && !s.IsSpellSlot && s.Side != side
                            && gameManager.Board.GetBoard(s.Side).lanes[s.LaneIndex] != null)
                .ToList();

            TargetPicker.RequestTarget(validTargets, chosen =>
            {
                if (chosen == null) return;

                gameManager.ApplyDamageToLane(chosen.Side, chosen.LaneIndex, GameRules.RemoveSpellDamage);
                SyncDeadMinions();
            });
        }

        if (playedCard.HasKeyword(CardKeyword.Buff))
        {
            gameManager.ApplyTurnEndBuffToAllies(side, GameRules.BuffSpellAttackBonus);
        }
    }

    // 전투 페이즈가 끝난 뒤(EndTurn 직후) 호출. 데이터상 죽어서 lanes[i]가 null이 됐는데
    // 아직 비주얼 카드가 필드에 남아있는 슬롯을 찾아서 그 오브젝트를 파괴함
    private void SyncDeadMinions()
    {
        foreach (var slot in fieldSlots)
        {
            // 마법 슬롯은 occupyingCard가 애초에 절대 안 채워지므로(TryPlaceMinionOnLane에서만 세팅됨)
            // 사실상 이 continue에서 안 걸리는 게 정상이지만, 혹시 나중에 마법 슬롯에도
            // occupyingCard를 쓰는 코드가 생기는 실수를 대비해 명시적으로 한 번 더 막아둠
            // (안 막으면 board.lanes[-1] 인덱싱으로 바로 터짐)
            if (slot == null || slot.IsSpellSlot || slot.occupyingCard == null) continue;

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

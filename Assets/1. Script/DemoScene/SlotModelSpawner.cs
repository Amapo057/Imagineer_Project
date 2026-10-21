using UnityEngine;

/// <summary>
/// 팀원(김지성)이 만들어둔 마커 인식/슬롯 판정 코드(CardSlot, TrackingMarker,
/// CardColliderManager 등)는 전혀 안 건드리고, 독립된 새 컴포넌트로 따로 두어서
/// 구현함. 슬롯 오브젝트에 이 컴포넌트를 추가로 붙이기만 하면 동작하고,
/// 기존 CardSlot 컴포넌트는 그대로 두고 건드리지 않음.
///
/// 판정 기준은 CardSlot.cs와 동일함(마커가 슬롯 트리거 안에 들어온 시점의
/// MarkerWorldPos.MarkerVersion을 기록해두고, 그 다음 버전이 바뀔 때까지(=새 검출
/// 1회 더) 같은 마커가 여전히 안에 있으면 "연속 2회 검출"로 보고 확정함).
///
/// 확정되면 CardDatabase.GetCardById(cardId)로 실제 CardData를 찾아 hologramPrefab을
/// 그대로 소환하고, 동시에 실제 게임 로직(GameManager)에도 반영함(TryPlayOnField 참고).
///
/// 손패(hand) 개념은 아예 안 씀 — 실제 보드게임은 가운데 마커 자리(=이 슬롯)에 물리 카드를
/// 내려놓는 방식뿐이라(카드를 손에 들고 있는 동안이 곧 손패), 디지털 손패 등록/소모 과정
/// 자체가 필요 없음. DemoTurnController.TryPlayCard와 달리 cardVisual/hand.Remove 같은
/// 처리가 없는 이유도 이것 때문.
///
/// hologramPrefab이 아직 안 들어간 카드(지금 대부분)는 모델만 조용히 안 뜨고, 게임 로직
/// 등록(코스트 차감/라인 배치)은 그와 상관없이 그대로 동작함 — 나중에 다른 챔피언 모델이
/// 나오면 CardDatabase 안의 해당 CardData.hologramPrefab만 채워주면 되고, 이 스크립트는
/// 손댈 필요 없이 바로 소환됨.
///
/// 사용법: 슬롯 오브젝트(CardSlotPrefabs 등, "Card" 태그가 붙은 마커 콜라이더를
/// 감지할 트리거 콜라이더가 있는 오브젝트)에 이 컴포넌트를 추가로 붙이고,
/// Card Database 필드에 Assets/2. Data/Card/CardDatabase.asset을,
/// Marker World Pos 필드에 씬에 있는 MarkerWorldPos를,
/// Lane Index/Side는 이 슬롯이 담당하는 필드 라인 번호와 편을 인스펙터에서 지정하면 끝.
/// </summary>
public class SlotModelSpawner : MonoBehaviour
{
    [Tooltip("마커 cardId로 실제 카드 정보(모델 등)를 찾기 위한 데이터베이스. " +
             "Assets/2. Data/Card/CardDatabase.asset을 끌어다 놓으면 됨")]
    [SerializeField] private CardDatabase cardDatabase;

    [Tooltip("마커 좌표/버전 정보를 받아오는 MarkerWorldPos. CardSlot이 쓰는 것과 같은 걸 넣으면 됨")]
    [SerializeField] private MarkerWorldPos markerWorldPos;

    [Header("필드 라인 정보 (실제 게임 로직 연동용)")]
    [Tooltip("이 슬롯이 담당하는 필드 라인 번호 (0~3). PlayerBoardState.lanes 인덱스와 똑같이 맞춰야 함")]
    [Range(0, 3)]
    [SerializeField] private int laneIndex = 0;

    [Tooltip("이 슬롯이 내 필드인지 상대 필드인지. 지금은 4개 슬롯 전부 Me(내 필드)로 씀 " +
             "— 상대 쪽 필드는 아직 물리 마커로 받는 자리가 없음")]
    [SerializeField] private PlayerSide side = PlayerSide.Me;

    [Header("홀로그램 표시 크기")]
    [Tooltip("소환되는 홀로그램 모델의 표시 배율. 원본 모델(사람 크기, 1~2m)을 테이블 위 " +
             "카드 크기에 맞는 미니어처로 보이게 줄이는 용도. 1.0이면 원본 크기 그대로(너무 큼), " +
             "값을 낮출수록 작아짐 — 필요하면 인스펙터에서 바로 조절 가능")]
    [Range(0.01f, 1f)]
    [SerializeField] private float hologramScale = 0.1f;

    // 씬에 있는 모든 SlotModelSpawner + TurnController(End Turn 버튼)가 같이 공유하는 게임 로직
    // 인스턴스. GameManager는 MonoBehaviour가 아니라 순수 C# 클래스라(DemoTurnController 참고)
    // 인스펙터로 연결할 수 없고, 턴/코스트/라인 상태를 슬롯마다 따로 들고 있으면 전부 다른
    // 게임이 되어버리므로 반드시 하나만 만들어서 공유해야 함 — VRGameSession이 그 하나를 들고 있음.
    // (예전엔 이 클래스가 직접 static GameManager를 들고 있었는데, End Turn 버튼이 생기면서
    // 그쪽에서도 같은 인스턴스가 필요해져서 VRGameSession으로 분리함)
    private static GameManager Game => VRGameSession.Game;

    // 지금 이 슬롯에서 확정 인식된 카드 id(없으면 -1)와, 그 카드로 소환해둔 모델 인스턴스.
    // 같은 카드가 계속 트리거 안에 있는 동안은 중복 소환하지 않기 위해 기록해둠
    private int confirmedCardId = -1;
    private GameObject spawnedModel;

    // 게임 로직(GameManager.Board)에 실제로 등록까지 성공한 카드 id (없으면 -1).
    // confirmedCardId와 별개로 두는 이유: 코스트 부족/라인이 이미 찬 경우처럼 "모델은 떴지만
    // 게임 로직에는 등록 안 된" 상태가 있을 수 있어서, 사망 감지(Update 참고)는 이 값으로만 판단함
    private int registeredCardId = -1;

    // 아직 확정 전(첫 번째 검출)인 후보 카드 정보
    private int pendingCardId = -1;
    private uint pendingMarkerVersion;
    private bool isPending = false;

    void Awake()
    {
        if (cardDatabase == null)
            Debug.LogWarning($"[SlotModelSpawner] '{name}'에 Card Database가 연결 안 되어 있습니다. "
                + "Assets/2. Data/Card/CardDatabase.asset을 끌어다 놓아야 홀로그램이 뜹니다.", this);
        if (markerWorldPos == null)
            Debug.LogWarning($"[SlotModelSpawner] '{name}'에 Marker World Pos가 연결 안 되어 있습니다. "
                + "씬의 MarkerWorldPos를 끌어다 놓아야 카드 확정 판정이 동작합니다.", this);
    }

    void Update()
    {
        // 전투 결과로 이 라인의 하수인이 죽었는데, 물리 카드는 치우지 않아서(실제 종이 카드가
        // 테이블 위에 그대로 있어서) OnTriggerExit가 안 불리는 경우를 위한 보정.
        // 게임 로직상 죽으면(라인이 비게 되면) 홀로그램도 같이 사라지게 함
        if (registeredCardId == -1) return;

        var board = Game.Board.GetBoard(side);
        bool stillOnField = laneIndex >= 0 && laneIndex < board.lanes.Length && board.lanes[laneIndex] != null;
        if (!stillOnField)
        {
            registeredCardId = -1;
            ClearModel();
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Card")) return;
        if (!other.TryGetComponent(out TrackingMarker cardInfo)) return;
        if (isPending || confirmedCardId == cardInfo.TargetCardId) return;

        pendingCardId = cardInfo.TargetCardId;
        pendingMarkerVersion = markerWorldPos != null ? markerWorldPos.MarkerVersion : 0;
        isPending = true;
    }

    void OnTriggerStay(Collider other)
    {
        if (!isPending) return;
        if (!other.CompareTag("Card")) return;
        if (!other.TryGetComponent(out TrackingMarker cardInfo)) return;

        // 이전과 다른 마커가 들어오면 후보 초기화
        if (pendingCardId != cardInfo.TargetCardId)
        {
            isPending = false;
            return;
        }

        // 마커 버전이 바뀌었다면(=새 검출 1회 더 지났다면) 연속 2회로 보고 확정
        if (markerWorldPos != null && pendingMarkerVersion != markerWorldPos.MarkerVersion)
        {
            Confirm(cardInfo.TargetCardId);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!other.TryGetComponent(out TrackingMarker cardInfo)) return;
        if (cardInfo.TargetCardId != confirmedCardId && cardInfo.TargetCardId != pendingCardId) return;

        isPending = false;
        pendingCardId = -1;

        if (cardInfo.TargetCardId == confirmedCardId)
        {
            // 주의: 물리 카드를 치워도 이미 게임 로직(GameManager.Board)에 등록된 내용은
            // 되돌리지 않음(코스트 환불/라인 비우기 안 함) — 실제 카드게임에서 "낸 카드를
            // 다시 집어도 판정은 그대로"인 것과 같은 동작. 지금은 홀로그램(비주얼)만 치움
            ClearModel();
        }
    }

    private void Confirm(int cardId)
    {
        isPending = false;
        confirmedCardId = cardId;
        SpawnModel(cardId);
        TryPlayOnField(cardId);
    }

    // cardId에 해당하는 CardData를 찾아 hologramPrefab을 띄움. 아직 모델이 안 배정된 카드는
    // hologramPrefab이 null이라 조용히 아무 일도 안 함(DemoTurnController.SpawnHologram과 동일한
    // 안전한 기본값) — 나중에 CardDatabase 쪽에 모델만 채워주면 코드 수정 없이 바로 소환됨
    private void SpawnModel(int cardId)
    {
        if (cardDatabase == null) return;

        CardData data = cardDatabase.GetCardById(cardId);
        if (data == null || data.hologramPrefab == null) return;

        ClearModel();
        spawnedModel = Instantiate(data.hologramPrefab, transform.position, transform.rotation, transform);

        // 슬롯 상위 계층(Table 등)에 의도치 않은 스케일이 걸려 있어도 홀로그램만은
        // 항상 일정한 크기로 보이도록 보정. 씬의 다른 오브젝트(콜라이더, 디버그 UI 등)는 건드리지 않음.
        // 1) 부모의 실제 월드 스케일을 먼저 상쇄해서 "원본 1:1 크기" 기준을 맞추고,
        // 2) 거기에 hologramScale을 곱해서 사람 크기 모델을 카드 위 미니어처 크기로 줄임.
        Vector3 parentLossyScale = transform.lossyScale;
        Vector3 modelScale = spawnedModel.transform.localScale;
        spawnedModel.transform.localScale = new Vector3(
            (parentLossyScale.x != 0 ? modelScale.x / parentLossyScale.x : modelScale.x) * hologramScale,
            (parentLossyScale.y != 0 ? modelScale.y / parentLossyScale.y : modelScale.y) * hologramScale,
            (parentLossyScale.z != 0 ? modelScale.z / parentLossyScale.z : modelScale.z) * hologramScale);
    }

    private void ClearModel()
    {
        if (spawnedModel != null)
        {
            Destroy(spawnedModel);
            spawnedModel = null;
        }
        confirmedCardId = -1;
    }

    // 확정된 카드를 실제 게임 로직(GameManager)에 반영: 지금 내 턴이고, 코스트가 충분하고,
    // 이 라인이 비어있으면 하수인으로 등록하고 코스트를 깎음. 마법 카드나 조건이 하나라도
    // 안 맞으면 조용히 아무 일도 안 함(홀로그램만 뜨고 게임 상태는 그대로 — 플레이어 입장에선
    // "카드를 냈는데 턴이 안 넘어갔다"처럼 보여서 자연스럽게 다시 시도하거나 다른 카드를 내게 됨).
    //
    // 손패(board.hand)는 거치지 않음 — 위 클래스 설명 참고, 물리 카드 자체가 손패라서
    // 디지털 손패 등록/소모 과정이 필요 없음. DemoTurnController.TryPlayCard와 달리
    // cardVisual/CardView도 필요 없음(물리 카드가 비주얼이므로)
    private void TryPlayOnField(int cardId)
    {
        if (cardDatabase == null) return;

        CardData data = cardDatabase.GetCardById(cardId);
        if (data == null) return;

        // 마법 카드는 라인에 들어가는 개념이 아니라(별도의 마법 전용 슬롯 필요) 여기선 처리 안 함
        // — VR용 마법 전용 자리가 따로 생기면 그때 추가
        if (data.cardType != CardType.Unit) return;

        GameManager game = Game;
        if (game.IsGameOver) return;
        if (game.State.CurrentPlayer != side) return; // 지금 내 턴이 아니면 못 냄

        var board = game.Board.GetBoard(side);
        if (board.currentCost < data.cost) return;
        if (board.lanes[laneIndex] != null) return; // 이미 이 라인에 하수인이 있음

        var minion = new CardInstance(data, side);
        if (!board.TryPlaceOnLane(minion, laneIndex)) return;

        board.currentCost -= data.cost;
        registeredCardId = cardId;

        // 전투의함성: 회복/앞의 적 피해만 여기서 처리함(DemoTurnController.ResolveBattlecry 참고).
        // 드로우는 손패가 없어서 의미가 없어 생략하고, 무기는 적 하수인을 지목할 VR용 입력
        // 방식이 아직 없어서(IFieldTargetPicker의 VR 구현이 없음) 지금은 그냥 보류함
        int amount = minion.currentAttack;
        if (data.HasKeyword(CardKeyword.Heal))
        {
            game.ApplyHealToLeftAlly(side, laneIndex, amount);
        }
        if (data.HasKeyword(CardKeyword.FrontDamage))
        {
            game.ApplyFrontDamage(side, laneIndex, amount);
        }
    }
}

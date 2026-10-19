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
/// 그대로 소환함. 코스트/턴/손패(GameManager.Board.hand) 검증은 아직 안 함 —
/// 이 전체를 만들려면 물리 카드를 손패로 등록시켜주는 별도 작업이 먼저 필요해서,
/// 일단은 "슬롯에 놓으면 모델만 위로 뜬다"까지만 담당하는 틀(스켈레톤)로 먼저 둠.
///
/// hologramPrefab이 아직 안 들어간 카드(지금 대부분)는 조용히 아무 일도 안 함
/// (DemoTurnController.SpawnHologram과 동일한 안전한 기본값) — 나중에 다른 챔피언
/// 모델이 나오면 CardDatabase 안의 해당 CardData.hologramPrefab만 채워주면 되고,
/// 이 스크립트는 손댈 필요 없이 바로 소환됨.
///
/// 사용법: 슬롯 오브젝트(CardSlotPrefabs 등, "Card" 태그가 붙은 마커 콜라이더를
/// 감지할 트리거 콜라이더가 있는 오브젝트)에 이 컴포넌트를 추가로 붙이고,
/// Card Database 필드에 Assets/2. Data/Card/CardDatabase.asset을,
/// Marker World Pos 필드에 씬에 있는 MarkerWorldPos를 끌어다 놓으면 끝.
/// </summary>
public class SlotModelSpawner : MonoBehaviour
{
    [Tooltip("마커 cardId로 실제 카드 정보(모델 등)를 찾기 위한 데이터베이스. " +
             "Assets/2. Data/Card/CardDatabase.asset을 끌어다 놓으면 됨")]
    [SerializeField] private CardDatabase cardDatabase;

    [Tooltip("마커 좌표/버전 정보를 받아오는 MarkerWorldPos. CardSlot이 쓰는 것과 같은 걸 넣으면 됨")]
    [SerializeField] private MarkerWorldPos markerWorldPos;

    // 지금 이 슬롯에서 확정 인식된 카드 id(없으면 -1)와, 그 카드로 소환해둔 모델 인스턴스.
    // 같은 카드가 계속 트리거 안에 있는 동안은 중복 소환하지 않기 위해 기록해둠
    private int confirmedCardId = -1;
    private GameObject spawnedModel;

    // 아직 확정 전(첫 번째 검출)인 후보 카드 정보
    private int pendingCardId = -1;
    private uint pendingMarkerVersion;
    private bool isPending = false;

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
            ClearModel();
        }
    }

    private void Confirm(int cardId)
    {
        isPending = false;
        confirmedCardId = cardId;
        SpawnModel(cardId);
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
}

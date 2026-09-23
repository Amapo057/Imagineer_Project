using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// "자세히 보기" 상태로 들어갈지 말지를 판단해서 CardView.SetDetailMode를 호출해주는 트리거.
///
/// 기술 문서상 실제 MR에서는 "손으로 카드를 들고 얼굴 가까이 가져가면" 발동하는 상태인데, 그
/// 판정(손 인식 + 카메라와의 거리)은 아직 준비 안 된 별도 시스템 담당이라 여기서는 PC 데모용으로
/// 마우스 호버로 대신 흉내냄: 카드 위에 마우스를 올리면 자세히 보기 시작, 벗어나면 종료.
///
/// IFieldTargetPicker/MouseFieldTargetPicker와 같은 패턴 — 나중에 VR로 넘어갈 때 이 컴포넌트만
/// "카드를 들고 있는지 + 얼마나 가까운지" 판정하는 구현체로 통째로 갈아끼우면 되고, CardView나
/// 카드 프리팹 쪽(detailPanel/illustrationImage)은 전혀 안 건드려도 됨.
/// </summary>
public class CardDetailViewer : MonoBehaviour
{
    [SerializeField] private Camera cam;

    [Tooltip("CardMove가 쓰는 것과 같은 카드 레이어를 그대로 꽂으면 됨")]
    [SerializeField] private LayerMask cardLayer;

    private CardView currentlyDetailed;

    void Update()
    {
        if (cam == null || Mouse.current == null) return;

        CardView hovered = null;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, cardLayer))
        {
            hovered = hit.collider.GetComponentInParent<CardView>();
        }

        if (hovered == currentlyDetailed) return; // 호버 대상이 그대로면 매 프레임 다시 켤 필요 없음

        if (currentlyDetailed != null) currentlyDetailed.SetDetailMode(false);
        if (hovered != null) hovered.SetDetailMode(true);

        currentlyDetailed = hovered;
    }
}

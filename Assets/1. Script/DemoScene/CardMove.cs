using UnityEngine;
using UnityEngine.InputSystem;

public class CardMove : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask cardLayer;
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("카드를 낼 때 코스트 소모 + 실제 하수인 등록을 시키기 위한 참조. " +
             "비워두면 예전처럼 필드 슬롯 판정 없이 그냥 이동만 함")]
    [SerializeField] private DemoTurnController demoTurnController;

    [Tooltip("놓은 위치 주변에서 FieldSlot을 찾을 반경. 필드 슬롯 콜라이더 크기에 맞춰서 넉넉하게 잡아둠")]
    [SerializeField] private float fieldSlotSearchRadius = 0.5f;

    private Transform selectedCard;

    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // 마우스 위치에 레이 생성
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

            if (selectedCard == null)
            {
                // 카드 레이어까지 레이 발사
                if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, cardLayer))
                {
                    selectedCard = hit.collider.transform;
                    Debug.Log($"[CardMove] 카드 집음: {selectedCard.name}");
                }
                else
                {
                    Debug.Log("[CardMove] 카드 레이어에 아무것도 안 맞음 (집기 실패)");
                }
            }
            // 카드 들고있으면 다음 클릭시 해당 위치로 이동
            else
            {
                if(Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
                {
                    Vector3 pos = hit.point;
                    pos.y += 0.05f;
                    Debug.Log($"[CardMove] 바닥 히트: {hit.collider.name} @ {pos}");

                    bool canPlace;
                    if (demoTurnController != null)
                    {
                        // 필드 슬롯 위가 아니면(그냥 바닥이면) 낼 수 없음 — 카드는 반드시 필드 자리에 내야
                        // 코스트를 쓰고 실제 하수인으로 등록됨
                        FieldSlot slot = FindFieldSlot(pos);
                        Debug.Log(slot != null
                            ? $"[CardMove] FieldSlot 찾음: lane={slot.LaneIndex} side={slot.Side}"
                            : "[CardMove] FieldSlot 못 찾음");
                        canPlace = slot != null && demoTurnController.TryPlaceMinion(slot, selectedCard.gameObject);
                        Debug.Log($"[CardMove] TryPlaceMinion 결과: {canPlace}");
                    }
                    else
                    {
                        // 참조가 없으면 예전처럼 아무 제약 없이 이동만 허용 (하위 호환)
                        canPlace = true;
                    }

                    if (canPlace)
                    {
                        selectedCard.position = pos;
                    }

                    // 못 냈어도 들고 있던 카드는 놓아줌(취소)
                    selectedCard = null;
                }
                else
                {
                    Debug.Log("[CardMove] 바닥 레이어에 아무것도 안 맞음");
                }
            }
        }
    }

    // pos 주변에서 FieldSlot 컴포넌트를 가진 콜라이더를 찾음 (레이어 상관없이 전부 검사).
    // FieldSlot의 콜라이더는 Is Trigger가 켜져 있어서, 프로젝트의 Physics 설정(Queries Hit Triggers)이
    // 꺼져 있으면 기본 OverlapSphere로는 아예 안 잡힘 — QueryTriggerInteraction.Collide로 그 설정과
    // 무관하게 항상 트리거도 잡히도록 명시함
    private FieldSlot FindFieldSlot(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, fieldSlotSearchRadius, ~0, QueryTriggerInteraction.Collide);
        foreach (var col in hits)
        {
            var slot = col.GetComponentInParent<FieldSlot>();
            if (slot != null) return slot;
        }
        return null;
    }
}

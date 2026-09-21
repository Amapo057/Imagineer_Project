using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// IFieldTargetPicker의 PC 목업용 구현체. 유효 대상 슬롯 중 하나를 마우스 왼쪽 클릭으로 고름
/// (우클릭하면 선택 취소). 나중에 VR로 넘어가면 이 파일만 손 터치 기반 구현체로 교체하면 되고,
/// 카드 효과 쪽 코드(DemoTurnController)는 전혀 건드릴 필요 없음.
///
/// 대상 탐색 방식은 CardMove.FindFieldSlot과 동일함(클릭 지점 주변 OverlapSphere로 가장 가까운
/// FieldSlot 찾기) — FieldSlot 콜라이더가 트리거라서 QueryTriggerInteraction.Collide를 명시함.
/// </summary>
public class MouseFieldTargetPicker : MonoBehaviour, IFieldTargetPicker
{
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask groundLayer;

    [Tooltip("클릭한 위치 주변에서 FieldSlot을 찾을 반경. CardMove의 fieldSlotSearchRadius와 맞춰두면 됨")]
    [SerializeField] private float fieldSlotSearchRadius = 0.5f;

    private List<FieldSlot> pendingTargets;
    private Action<FieldSlot> pendingCallback;

    public void RequestTarget(List<FieldSlot> validTargets, Action<FieldSlot> onTargetChosen)
    {
        pendingTargets = validTargets;
        pendingCallback = onTargetChosen;

        // 고를 수 있는 대상이 애초에 없으면(적이 하나도 없을 때) 입력을 기다릴 필요 없이 바로 무효 처리
        if (pendingTargets == null || pendingTargets.Count == 0)
        {
            Finish(null);
        }
    }

    void Update()
    {
        if (pendingCallback == null) return; // 대상 선택을 기다리는 중이 아니면 아무 것도 안 함

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Finish(null); // 우클릭으로 선택 취소
            return;
        }

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
            {
                FieldSlot slot = FindNearbyTarget(hit.point);
                if (slot != null) Finish(slot);
                // 유효 대상이 아닌 곳을 클릭하면 그냥 무시하고 계속 대상 선택 대기
            }
        }
    }

    // pendingTargets 안에 있는 슬롯만 후보로 두고, 클릭 지점(pos)에서 가장 가까운 걸 고름
    private FieldSlot FindNearbyTarget(Vector3 pos)
    {
        Collider[] hits = Physics.OverlapSphere(pos, fieldSlotSearchRadius, ~0, QueryTriggerInteraction.Collide);

        FieldSlot closest = null;
        float closestSqrDist = float.MaxValue;

        foreach (var col in hits)
        {
            var slot = col.GetComponentInParent<FieldSlot>();
            if (slot == null || !pendingTargets.Contains(slot)) continue;

            float sqrDist = (slot.transform.position - pos).sqrMagnitude;
            if (sqrDist < closestSqrDist)
            {
                closestSqrDist = sqrDist;
                closest = slot;
            }
        }

        return closest;
    }

    private void Finish(FieldSlot chosen)
    {
        var callback = pendingCallback;
        pendingTargets = null;
        pendingCallback = null;
        callback?.Invoke(chosen);
    }
}

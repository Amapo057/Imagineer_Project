using System.Collections.Generic;
using UnityEngine;

// 플레이어가 건물 등에 닿았을 때 전기 상호작용을 발생시키는 감지기
//
// 트리거 이벤트는 양쪽 중 최소 하나에 Rigidbody가 있어야 발생한다.
// 플레이어는 스플라인을 따라 transform.position으로 직접 움직이므로
// 물리에 끌려다니지 않도록 Kinematic Rigidbody를 사용한다.
[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class ElectricInteractionDetector : MonoBehaviour
{
    [Header("상호작용 설정")]
    [SerializeField] private bool canInteract = true;

    [Tooltip("감지 범위입니다. 건물에 닿았다고 판정할 거리입니다.")]
    [SerializeField] private float detectionRadius = 2f;

    [Header("상호작용 방식")]
    [Tooltip("켜면 범위 안에서 키를 눌러야 작동합니다. 끄면 닿기만 해도 즉시 작동합니다.")]
    [SerializeField] private bool requireKeyPress = true;

    [Tooltip("상호작용에 사용할 키입니다.")]
    [SerializeField] private KeyCode interactionKey = KeyCode.F;

    [Tooltip("켜면 범위 안의 모든 대상이 한 번에 반응합니다. 끄면 가장 가까운 대상만 반응합니다.")]
    [SerializeField] private bool interactAllInRange = false;

    [Tooltip("같은 대상에 다시 상호작용하기까지 기다리는 시간입니다.")]
    [SerializeField] private float interactionCooldown = 0.2f;

    [Header("감지 대상 제한")]
    [Tooltip("여기 포함된 레이어만 검사합니다. 건물 레이어만 켜두면 불필요한 검사가 줄어듭니다.")]
    [SerializeField] private LayerMask detectionLayers = ~0;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = false;
    [SerializeField] private bool drawGizmos = true;

    // 현재 범위 안에 들어와 있는 대상들
    private readonly Dictionary<Collider, IElectricInteractable> targetsInRange =
        new Dictionary<Collider, IElectricInteractable>();

    // 대상별로 마지막 상호작용 시각을 따로 기록한다
    // 하나의 타이머를 공유하면 여러 건물을 빠르게 스칠 때 일부가 누락된다
    private readonly Dictionary<Collider, float> lastContactTimes = new Dictionary<Collider, float>();

    // 재사용해서 매번 새 리스트를 만들지 않도록 한다
    private readonly List<Collider> removeBuffer = new List<Collider>();

    // UI에서 "F를 누르세요" 안내를 띄우고 싶을 때 사용한다
    public bool HasTargetInRange => targetsInRange.Count > 0;

    private void Reset()
    {
        SetupComponents();
    }

    private void Awake()
    {
        SetupComponents();
    }

    // 컴포넌트를 붙이기만 하면 동작하도록 필요한 물리 설정을 스스로 맞춘다
    // 프리팹에 이미 직렬화된 경우 RequireComponent가 자동 추가를 보장하지 않으므로 직접 확인한다
    private void SetupComponents()
    {
        SphereCollider triggerCollider = GetComponent<SphereCollider>();

        if (triggerCollider == null)
        {
            triggerCollider = gameObject.AddComponent<SphereCollider>();
        }

        triggerCollider.isTrigger = true;
        triggerCollider.radius = detectionRadius;

        Rigidbody body = GetComponent<Rigidbody>();

        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        // 직접 위치를 옮기는 캐릭터이므로 물리 시뮬레이션에서 제외한다
        body.isKinematic = true;
        body.useGravity = false;
    }

    private void Update()
    {
        if (!requireKeyPress || !canInteract)
        {
            return;
        }

        if (!Input.GetKeyDown(interactionKey))
        {
            return;
        }

        InteractWithTargetsInRange();
    }

    private void OnTriggerEnter(Collider other)
    {
        RegisterTarget(other, true);
    }

    private void OnTriggerStay(Collider other)
    {
        RegisterTarget(other, false);
    }

    private void OnTriggerExit(Collider other)
    {
        // 기록이 무한정 쌓이지 않도록 정리한다
        targetsInRange.Remove(other);
        lastContactTimes.Remove(other);
    }

    // 범위 안의 대상을 목록에 넣는다
    // 키 입력 방식이 아니면 여기서 바로 상호작용까지 처리한다
    private void RegisterTarget(Collider other, bool isFirstContact)
    {
        if (!canInteract || other == null)
        {
            return;
        }

        // 레이어 필터에 걸리지 않으면 검사 자체를 건너뛴다
        if ((detectionLayers.value & (1 << other.gameObject.layer)) == 0)
        {
            return;
        }

        // 이미 등록된 대상이면 다시 찾지 않는다
        if (!targetsInRange.TryGetValue(other, out IElectricInteractable interactable))
        {
            interactable = other.GetComponent<IElectricInteractable>();

            if (interactable == null)
            {
                interactable = other.GetComponentInParent<IElectricInteractable>();
            }

            if (interactable == null)
            {
                return;
            }

            targetsInRange[other] = interactable;
        }

        if (requireKeyPress)
        {
            return;
        }

        // 즉시 작동 방식일 때만 여기서 처리한다
        if (!isFirstContact && IsOnCooldown(other))
        {
            return;
        }

        Interact(other, interactable);
    }

    private void InteractWithTargetsInRange()
    {
        CleanupDestroyedTargets();

        if (targetsInRange.Count == 0)
        {
            if (showDebugLog)
            {
                Debug.Log("[Electric Interaction] 범위 안에 대상이 없습니다.");
            }

            return;
        }

        if (interactAllInRange)
        {
            foreach (KeyValuePair<Collider, IElectricInteractable> pair in targetsInRange)
            {
                if (IsOnCooldown(pair.Key))
                {
                    continue;
                }

                Interact(pair.Key, pair.Value);
            }

            return;
        }

        // 가장 가까운 대상 하나만 처리한다
        Collider nearestCollider = null;
        IElectricInteractable nearestTarget = null;
        float nearestDistance = float.MaxValue;

        foreach (KeyValuePair<Collider, IElectricInteractable> pair in targetsInRange)
        {
            if (IsOnCooldown(pair.Key))
            {
                continue;
            }

            // 콜라이더 표면에서 가장 가까운 지점까지의 거리로 판단한다
            float distance = Vector3.Distance(
                transform.position,
                pair.Key.ClosestPoint(transform.position)
            );

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestCollider = pair.Key;
                nearestTarget = pair.Value;
            }
        }

        if (nearestTarget != null)
        {
            Interact(nearestCollider, nearestTarget);
        }
    }

    private void Interact(Collider targetCollider, IElectricInteractable interactable)
    {
        interactable.OnElectricContact(this);
        lastContactTimes[targetCollider] = Time.time;

        if (showDebugLog)
        {
            Debug.Log("[Electric Interaction] 전기 상호작용 발생: " + targetCollider.gameObject.name);
        }
    }

    private bool IsOnCooldown(Collider target)
    {
        if (!lastContactTimes.TryGetValue(target, out float lastTime))
        {
            return false;
        }

        return Time.time - lastTime < interactionCooldown;
    }

    // 파괴된 오브젝트가 목록에 남아있으면 정리한다
    // OnTriggerExit은 대상이 파괴될 때 호출되지 않을 수 있다
    private void CleanupDestroyedTargets()
    {
        removeBuffer.Clear();

        foreach (KeyValuePair<Collider, IElectricInteractable> pair in targetsInRange)
        {
            if (pair.Key == null)
            {
                removeBuffer.Add(pair.Key);
            }
        }

        foreach (Collider target in removeBuffer)
        {
            targetsInRange.Remove(target);
            lastContactTimes.Remove(target);
        }
    }

    private void OnValidate()
    {
        // 인스펙터에서 반경을 바꾸면 콜라이더에도 바로 반영되도록 한다
        SphereCollider triggerCollider = GetComponent<SphereCollider>();

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
            triggerCollider.radius = detectionRadius;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
}

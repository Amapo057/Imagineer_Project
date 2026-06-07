using UnityEngine;

public class ElectricInteractionDetector : MonoBehaviour
{
    [Header("상호작용 설정")]
    [SerializeField] private bool canInteract = true;

    [Tooltip("같은 대상에 너무 자주 상호작용하지 않도록 막는 시간입니다.")]
    [SerializeField] private float interactionCooldown = 0.2f;

    [Header("디버그")]
    [SerializeField] private bool showDebugLog = true;

    private float lastInteractionTime;

    private void OnTriggerEnter(Collider other)
    {
        TryInteract(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryInteract(other);
    }

    private void TryInteract(Collider other)
    {
        if (!canInteract)
        {
            return;
        }

        if (Time.time - lastInteractionTime < interactionCooldown)
        {
            return;
        }

        IElectricInteractable interactable = other.GetComponent<IElectricInteractable>();

        if (interactable == null)
        {
            interactable = other.GetComponentInParent<IElectricInteractable>();
        }

        if (interactable == null)
        {
            return;
        }

        interactable.OnElectricContact(this);
        lastInteractionTime = Time.time;

        if (showDebugLog)
        {
            Debug.Log("[Electric Interaction] 전기 상호작용 실행: " + other.gameObject.name);
        }
    }
}
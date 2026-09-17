using UnityEngine;

public class OnCardChecker : MonoBehaviour
{
    private int triggerCount;

    // 물리 트리거 감지 + 코드에서 직접 점유 표시(manuallyOccupied) 둘 중 하나라도 있으면 "차있음".
    // DrawManager가 카드를 배치할 때 SetOccupied(true)를 직접 호출해서 확실히 점유시킴 —
    // 트리거 충돌 감지만 믿으면(리지드바디 유무 등 씬/프리팹 설정에 따라) 감지가 안 될 수 있어서,
    // 그럴 때 다음 카드도 계속 같은 자리에 겹쳐서 나오는 문제가 있었음
    private bool manuallyOccupied;

    public bool IsTriggered => triggerCount > 0 || manuallyOccupied;

    public void SetOccupied(bool occupied)
    {
        manuallyOccupied = occupied;
    }

    private void OnTriggerEnter(Collider other)
    {
        triggerCount++;
        Debug.Log("OnTrigger");
    }
    private void OnTriggerExit(Collider other)
    {
        triggerCount--;
    }
}

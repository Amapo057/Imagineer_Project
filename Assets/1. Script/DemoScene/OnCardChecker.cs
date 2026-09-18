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
    }
    private void OnTriggerExit(Collider other)
    {
        triggerCount--;

        // 카드가 실제로 이 자리를 벗어났다는 뜻이므로, DrawManager가 SetOccupied(true)로
        // 걸어뒀던 수동 점유 표시도 같이 풀어줘야 함. 안 풀면 한 번 쓰인 손패 자리는
        // 카드가 떠난 뒤에도 영원히 "차있음"으로 남아서, 자리가 8개 다 소진되면
        // Draw 버튼을 눌러도 더 이상 아무 카드도 안 나오는 버그가 있었음
        manuallyOccupied = false;
    }
}

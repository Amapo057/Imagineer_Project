using UnityEngine;

public class OnCardChecker : MonoBehaviour
{
    private int triggerCount;

    public bool IsTriggered => triggerCount > 0;

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

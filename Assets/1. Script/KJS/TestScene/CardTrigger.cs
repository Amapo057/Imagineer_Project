using UnityEngine;

public class CardTrigger : MonoBehaviour
{
    private int triggerCount;

    public bool IsTriggered => triggerCount > 0;

    private void OnTriggerEnter(Collider other)
    {
        triggerCount++;
    }
    private void OnTriggerExit(Collider other)
    {
        triggerCount--;
    }
}
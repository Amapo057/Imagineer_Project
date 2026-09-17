using UnityEngine;

public class CardFieldLocker : MonoBehaviour
{
    private bool isField;
    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Field"))
        {
            Vector3 pos = other.transform.position;
            pos.y += 0.05f;

            transform.position = pos;
            
            isField = true;
        }
    }
    private void OnTriggerExit(Collider other)
    {
        isField = false;
    }
    // 필드 여부 체크 함수
    public bool IsFied()
    {
        return isField;
    }
}

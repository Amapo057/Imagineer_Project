using UnityEngine;

public class CardFieldLocker : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if(other.CompareTag("Field"))
        {
            Vector3 pos = other.transform.position;
            pos.y += 0.05f;

            transform.position = pos;
            
        }
    }
}

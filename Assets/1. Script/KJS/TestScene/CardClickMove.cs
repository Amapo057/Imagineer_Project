using UnityEngine;
using UnityEngine.InputSystem;

public class CardClickMove : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private LayerMask cardLayer;
    [SerializeField] private LayerMask groundLayer;

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
                }
            }
            else
            {
                if(Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
                {
                    Vector3 pos = hit.point;
                    pos.y += 0.05f;

                    selectedCard.position = pos;
                    selectedCard = null;
                }
            }
        }
    }
}
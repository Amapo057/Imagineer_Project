using Unity.VisualScripting;
using UnityEngine;

public class DrawManager : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefabs;
    [SerializeField] private Transform handCheckers;

    private OnCardChecker[] checkersTrigger;

    // 게임 시작시 필드 위치 받아오기
    void Awake()
    {
        checkersTrigger = handCheckers.GetComponentsInChildren<OnCardChecker>();
    }

    // 버튼 누르면 왼쪽부터 빈자리 검사 후 카드 배치
    public void OnDrawButtonClick()
    {
        Vector3 emptyPosition = Vector3.zero;
        int i = 0;
        foreach(var checker in checkersTrigger)
        {
            if (!checker.IsTriggered)
            {
                emptyPosition = checker.transform.position;
                i = 0;
                break;
            }
            i++;
        }
        // 필드 꽉찼으면 안뽑기
        if (i > 7)
        {
            return;
        }
        Instantiate(cardPrefabs, emptyPosition, Quaternion.identity);
    
    }
}

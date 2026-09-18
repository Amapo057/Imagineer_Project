using System.Collections;
using UnityEngine;

public class DrawManager : MonoBehaviour
{
    [Tooltip("이 덱이 내 덱인지 상대 덱인지 — DemoTurnController가 지금 턴 플레이어에 맞는 " +
             "DrawManager를 찾는 데 씀 (MyDeck=Me, EnemyDeck=Opponent)")]
    [SerializeField] private PlayerSide side;
    public PlayerSide Side => side;

    [SerializeField] private GameObject cardPrefabs;
    [SerializeField] private Transform handCheckers;

    [Tooltip("카드가 덱(이 오브젝트) 위치에서 손패 자리까지 이동하는 데 걸리는 시간")]
    [SerializeField] private float drawMoveDuration = 0.4f;

    private OnCardChecker[] checkersTrigger;

    // 게임 시작시 필드 위치 받아오기
    void Awake()
    {
        checkersTrigger = handCheckers.GetComponentsInChildren<OnCardChecker>();
    }

    // 버튼 누르면 왼쪽부터 빈자리 검사 후 카드 배치
    public void OnDrawButtonClick()
    {
        OnCardChecker emptyChecker = null;
        Vector3 emptyPosition = Vector3.zero;
        int i = 0;
        foreach(var checker in checkersTrigger)
        {
            if (!checker.IsTriggered)
            {
                emptyChecker = checker;
                emptyPosition = checker.transform.position;
                i = 0;
                break;
            }
            i++;
        }
        // 필드 꽉찼으면 안뽑기
        if (i > 7 || emptyChecker == null)
        {
            return;
        }

        // 트리거 감지를 기다리지 않고 바로 점유 처리 — 안 그러면 감지가 늦거나 안 될 때
        // 다음 카드가 같은 자리에 계속 겹쳐서 나옴
        emptyChecker.SetOccupied(true);

        // 덱(이 오브젝트) 위치에서 카드를 생성해서 손패 자리까지 이동시키는 연출
        GameObject card = Instantiate(cardPrefabs, transform.position, Quaternion.identity);
        StartCoroutine(MoveCardToHand(card.transform, emptyPosition));
    }

    private IEnumerator MoveCardToHand(Transform card, Vector3 targetPosition)
    {
        Vector3 startPosition = card.position;
        float elapsed = 0f;

        while (elapsed < drawMoveDuration)
        {
            if (card == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / drawMoveDuration);
            card.position = Vector3.Lerp(startPosition, targetPosition, t);
            yield return null;
        }
        if (card != null) card.position = targetPosition;
    }
}

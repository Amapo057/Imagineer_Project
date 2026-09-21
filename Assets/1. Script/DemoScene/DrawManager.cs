using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawManager : MonoBehaviour
{
    [Tooltip("이 덱이 내 덱인지 상대 덱인지 — DemoTurnController가 지금 턴 플레이어에 맞는 " +
             "DrawManager를 찾는 데 씀 (MyDeck=Me, EnemyDeck=Opponent)")]
    [SerializeField] private PlayerSide side;
    public PlayerSide Side => side;

    [SerializeField] private GameObject cardPrefabs;
    [SerializeField] private Transform handCheckers;

    [Tooltip("카드가 이동하는 데 걸리는 시간 (덱→손패, 그리고 카드를 써서 나머지가 당겨질 때 둘 다 씀)")]
    [SerializeField] private float drawMoveDuration = 0.4f;

    // 손패 슬롯들의 "위치"만 순서대로 들고 있음. 예전에는 OnCardChecker의 트리거 감지로
    // 빈자리를 찾았는데, 그러면 카드를 하나 쓰고 나면 그 자리만 비고 나머지 카드는 그대로 있어서
    // 손패 중간에 구멍이 뚫린 것처럼 보였음. 이제는 카드를 "왼쪽부터 순서대로" 관리하는 리스트로
    // 바꿔서, 카드가 하나 빠지면 그 뒤 카드들을 전부 한 칸씩 당겨서 항상 빈칸 없이 모이게 함
    private Transform[] slotPositions;
    private readonly List<Transform> handCards = new List<Transform>();

    void Awake()
    {
        var checkers = handCheckers.GetComponentsInChildren<OnCardChecker>();
        slotPositions = new Transform[checkers.Length];
        for (int i = 0; i < checkers.Length; i++)
        {
            slotPositions[i] = checkers[i].transform;
        }
    }

    // 버튼(혹은 드로우 키워드)으로 카드를 뽑았을 때 호출. 손패 맨 뒤(현재 카드 수만큼 뒤) 자리에
    // 카드 한 장을 실제로 띄우고, 그 카드가 어떤 CardData인지 CardView에 넘겨서 이름/코스트/
    // 공격력/체력/능력 텍스트가 진짜 값으로 채워지도록 함(예전엔 이 정보가 아예 없어서 프리팹에
    // 박아둔 플레이스홀더 숫자만 보였음).
    //
    // data는 GameManager 쪽에서 실제로 덱에서 뽑힌 카드를 그대로 넘겨받는 것 — 이 메서드는
    // 순수하게 "그 카드를 화면에 보여주는" 비주얼 담당이고, 뽑을지 말지(덱이 비었는지, 손패가
    // 꽉 찼는지) 판단은 GameManager.TryDrawCard가 이미 끝낸 뒤에 호출됨
    public void OnDrawButtonClick(CardData data)
    {
        // 손패 꽉 찼으면 안 뽑기. 씬에 배치된 손패 슬롯 개수(slotPositions.Length)와 디자인 규칙상
        // 손패 최대 장수(GameRules.MaxHandSize) 중 더 작은 쪽을 진짜 한도로 씀 — 씬에 슬롯이
        // 실수로 더 많이/적게 있어도 항상 안전하게 동작하도록
        int effectiveLimit = Mathf.Min(slotPositions.Length, GameRules.MaxHandSize);
        if (handCards.Count >= effectiveLimit)
        {
            return;
        }

        GameObject card = Instantiate(cardPrefabs, transform.position, Quaternion.identity);

        var view = card.GetComponent<CardView>();
        if (view != null) view.SetCardData(data);

        handCards.Add(card.transform);

        Vector3 targetPosition = slotPositions[handCards.Count - 1].position;
        StartCoroutine(MoveCard(card.transform, targetPosition));
    }

    // 손패의 카드 하나가 실제로 사용(필드에 배치 등)돼서 더 이상 손패에 없을 때 호출.
    // 이 카드를 목록에서 빼고, 그 뒤에 있던 카드들을 전부 한 칸씩 왼쪽 자리로 당겨서
    // 빈칸 없이 다시 모이도록 함
    public void RemoveCardFromHand(GameObject card)
    {
        if (card == null) return;

        int index = handCards.IndexOf(card.transform);
        if (index < 0) return;

        handCards.RemoveAt(index);

        for (int i = index; i < handCards.Count; i++)
        {
            StartCoroutine(MoveCard(handCards[i], slotPositions[i].position));
        }
    }

    private IEnumerator MoveCard(Transform card, Vector3 targetPosition)
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

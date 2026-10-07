using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DeckSlot : MonoBehaviour
{
  [SerializeField] private TextMeshProUGUI debugNum;
  // 덱 관리에 적합한 해쉬셋 사용
  private HashSet<int> deckCards = new HashSet<int>();

  void OnTriggerEnter(Collider other)
  {
    // 검사
    if(!other.CompareTag("Card")) return;
    if(!other.TryGetComponent(out TrackingMarker cardInfo)) return;
    // 중복된 카드는 추가되지 않음
    if (deckCards.Add(cardInfo.TargetCardId))
    {
      debugNum.text = string.Join(",", deckCards);
    }
  }

  // 현재 뽑은 카드수 반환
  public int GetDeckCount()
  {
    return deckCards.Count;
  }
}

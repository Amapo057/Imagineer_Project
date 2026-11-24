using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DeckSlot : MonoBehaviour
{
  [SerializeField] private TextMeshProUGUI debugNum;
  [SerializeField] private DeckManager deckManager;
  // 덱 관리에 적합한 해쉬셋 사용
  private HashSet<int> deckCards = new HashSet<int>();

  void OnTriggerEnter(Collider other)
  {
    // 검사
    if(deckManager != null || !other.CompareTag("Card")) return;
    if(!other.TryGetComponent(out TrackingMarker cardInfo)) return;
    // 중복된 카드는 추가되지 않음
    if (deckCards.Add(cardInfo.TargetCardId) && debugNum != null)
    {
      debugNum.text = string.Join(",", deckCards);
    }
  }

  // 덱으로 등록된 고유 카드 수.
  public int GetDeckCount()
  {
    return deckManager != null ? deckManager.RegisteredCount : deckCards.Count;
  }
}

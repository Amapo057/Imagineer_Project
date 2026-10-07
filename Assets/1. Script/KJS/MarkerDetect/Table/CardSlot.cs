using TMPro;
using UnityEngine;

public class CardSlot : MonoBehaviour
{
  // [SerializeField] private int fieldNum = 0;
  [SerializeField] private TextMeshProUGUI debugNum;
  [SerializeField] private MarkerWorldPos markerWorldPos;
  private int triggerCardId;
  private uint triggerMarkerVersion;
  private bool isTriggerMarker = false;

  void OnTriggerEnter(Collider other)
  {
    // 검사
    if(!other.CompareTag("Card")) return;
    if(!other.TryGetComponent(out TrackingMarker cardInfo)) return;
    if (isTriggerMarker) return;
    
    triggerCardId = cardInfo.TargetCardId;
    triggerMarkerVersion = markerWorldPos.MarkerVersion;
    isTriggerMarker = true;
  }
  void OnTriggerStay(Collider other)
  {
    if(!isTriggerMarker) return;
    if(!other.CompareTag("Card")) return;
    if(!other.TryGetComponent(out TrackingMarker cardInfo)) return;
    // 이전과 마커번호 다를시 인식 초기화
    if(triggerCardId != cardInfo.TargetCardId)
    {
      triggerCardId = -1;
      isTriggerMarker = false;
      return;
    }
    // 마커 번호가 같고, 버전이 다를시 확정
    if(triggerMarkerVersion != markerWorldPos.MarkerVersion)
    {
      debugNum.text = cardInfo.TargetCardId.ToString();
    }
  }
}

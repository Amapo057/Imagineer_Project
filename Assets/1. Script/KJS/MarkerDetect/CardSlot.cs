using TMPro;
using UnityEngine;

public class CardSlot : MonoBehaviour
{
    [SerializeField] private int fieldNum = 0;
    [SerializeField] private TextMeshProUGUI debugNum;

    void OnTriggerEnter(Collider other)
    {
      if(!other.CompareTag("Card")) return;

      var cardInfo = other.GetComponent<TrackingMarker>();

      debugNum.text = cardInfo.GetCardNumber().ToString();

      
    }
}

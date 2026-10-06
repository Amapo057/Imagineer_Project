using System.Collections.Generic;
using UnityEngine;

public class CardColliderManager : MonoBehaviour
{
    // 좌표를 받을 함수와 인식한 카드에 붙일 프리팹 받기
    [SerializeField] private MarkerWorldPos markerWorldPos;
    [SerializeField] private GameObject cardColliderPrefab;
    [SerializeField] private HandManager handManager;
    [SerializeField] private DetailModeManager detailModeManager;

    // 인스턴스가 생성된 카드 관리용 딕셔너리 리스트
    private Dictionary<int, TrackingMarker> cardInstances = new();

    private uint lastMarkerVersion = 0;
    void Update()
    {
        if(markerWorldPos.TryGetMarkerResult(out var markerResults) && lastMarkerVersion != markerWorldPos.MarkerVersion)
        {
            foreach(var marker in markerResults)
            {
                // 중앙 기준점용 0번은 무시
                if(marker.markerId == 0) continue;
                // 리스트에 이미 마커가 있는지 검사
                if (cardInstances.ContainsKey(marker.markerId)) continue;
                // 홀수시 건너뛰기
                // if(marker.id % 2 != 0) continue;

                // 카드 콜라이더 생성
                CreateCardCollider(marker);
            }
            lastMarkerVersion = markerWorldPos.MarkerVersion;
        }
    }
    // 인자로 받은 마커를 활용해 콜라이더 배치
    private void CreateCardCollider(MarkerPositionResult marker)
    {
        // 인스턴스 생성
        GameObject obj = Instantiate(cardColliderPrefab, marker.worldPosition, marker.worldRotation);
        // 마커 추적 코드 받기
        TrackingMarker trackingMarker = obj.GetComponent<TrackingMarker>();
        // 타겟 아이디 설정
        trackingMarker.Initialize(marker.markerId, handManager, markerWorldPos, detailModeManager);               
        // 처리한 아이디는 리스트에 추가
        cardInstances.Add(marker.markerId, trackingMarker);
    }
}


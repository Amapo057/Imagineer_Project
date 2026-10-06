using UnityEngine;
using System.Collections.Generic;

public class DetailModeManager : MonoBehaviour
{
    // 마커 정보 받기위해 코드 연결
    [SerializeField] private MarkerWorldPos markerWorldPos;
    [SerializeField] private TMPro.TextMeshProUGUI debug2;
    // 코드로부터 받아 저장할 변수 선언
    private uint lastMarkerVersion = 0;
    public int LastMarkerId{get; private set;} = 0;
    public bool IsDetailMode{get; private set;} = false;
    private float detailModeRange = 0.2f;
    private int detailModeCounte = 0;


    void Update()
    {
        if (!IsDetailMode)
        {
            debug2.text = $"Not Detail{detailModeCounte}";
        }
        else
        {
            debug2.text = "detailMode";
        }
        // 마커가 새 버전이 없다면 검사하지 않음
        if(lastMarkerVersion == markerWorldPos.MarkerVersion) return;
        // 마커 정보 받고, 거리 측정
        if(markerWorldPos.TryGetMarkerResult(out var markerResult))
        {
            lastMarkerVersion = markerWorldPos.MarkerVersion;
            int nearestMarkerId = -1;
            // 더 작은 거리를 찾아야하니 최대값으로 초기화
            float nearestDistance = float.MaxValue;
            Vector3 cameraPosition = markerWorldPos.CameraPosition;

            foreach(var marker in markerResult)
            {
                // 0번 마커 무시
                if(marker.id == 0) continue;
                // 카메라와 마커 거리 측정 후 이전보다 더 가까우면 id와 거리 기록
                float distance = Vector3.Distance(cameraPosition, marker.worldPosition);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestMarkerId = marker.id;
                }

            }
            // 마커 id가 관측되지 않은 기본값이거나, 범위내에 없다면 인식 초기화
            if(nearestMarkerId == -1 || !IsMarkerInRange(nearestDistance))
            {
                LastMarkerId = -1;
                detailModeCounte = 0;
                IsDetailMode = false;
                return;
            }
            // 이전과 가장 가까운 마커 id가 같다면 카운트 상승
            if(LastMarkerId == nearestMarkerId)
            {
                if (!IsDetailMode)
                {
                    detailModeCounte++;
                }
            }
            // 가장 가까운 마커가 다르다면 id를 해당 마커로 기록 및 카운트 초기화와 디테일모드 종료
            else
            {
                LastMarkerId = nearestMarkerId;
                detailModeCounte = 1;
                IsDetailMode = false;
            }
            if(detailModeCounte >= 4 && !IsDetailMode)
            {
                IsDetailMode = true;
            }
        }
    }

    private bool IsMarkerInRange(float distance)
    {
        // 자세히보기 상태가 아닐경우 범위 측정
        if (!IsDetailMode)
        {
            if(distance <= detailModeRange)
            {
                return true;
            }
            return false;
        }
        // 자세히보기 모드 진입시 인식 범위 증가
        else
        {
            if(distance <= detailModeRange)
            {
                return true;
            }
            return false;
        }
        
    }
}

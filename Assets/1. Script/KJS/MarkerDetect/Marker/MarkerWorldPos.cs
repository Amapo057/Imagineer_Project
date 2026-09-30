using UnityEngine;
using OpenCvSharp;
using System.Collections.Generic;

public class MarkerWorldPos : MonoBehaviour
{    [SerializeField] private TMPro.TextMeshProUGUI debugText4;
    // 탐지 코드 연결
    [SerializeField] private MarkerDetecter markerDetecter;
    [SerializeField] private GameObject fieldRoot;

    // 마커 인식 당시 카메라 정보 저장용 변수
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    // 이번 마커들 저장용 리스트
    private List<MarkerPositionResult> markerPositionResults;

    // 새 마커 정보 세대 번호
    // 20년 작동시 오버플로우 발생하니 주의
    public uint MarkerVersion {get; private set;}

    void Update()
    {
        if(markerDetecter.TryGetMarkerResult(out var result))
        {
            // 마커 세대 업데이트
            MarkerVersion++;
            // 마커들 저장용 리스트 초기화
            markerPositionResults = new List<MarkerPositionResult>(result.Count);

            string idsText = "";

            // 마커 결과 리스트 순회하며 월드 좌표계로 변환 후 저장
            for(int i = 0; i < result.Count; i++)
            {
                // 인식한 id를 한줄로 정리
                idsText += result[i].id + ", ";
                // 첫번째 결과에서 카메라 좌표와 회전값을 가져와 저장
                if(i == 0)
                {
                    cameraPosition = result[i].cameraPosition;
                    cameraRotation = result[i].cameraRotation;
                }
                // 좌표계 변환 함수로 월드 좌표계로 변환
                (var markerWorldPosition, var markerWorldRotation) = LocalToWroldPos(result[i]);
                if(result[i].id != 0 && isEnemyField(markerWorldPosition)) continue;
                // 변환한 좌표를 id와 함께 리스트에 저장
                markerPositionResults.Add(new MarkerPositionResult{id = result[i].id, worldPosition = markerWorldPosition, worldRotation = markerWorldRotation});
            }
            // ui에 아이디 출력
            debugText4.text = idsText;
        }
    }
    private (Vector3 worldPosition, Quaternion worldRotation) LocalToWroldPos(MarkerDetectionResult result)
    {
        // --- 좌표 ---
        // opencv좌표계에서 유니티 좌표계로 변경하기 위해 y축 반전
        Vector3 yInversionPos = new Vector3((float)result.tvec[0], -(float)result.tvec[1], (float)result.tvec[2]);
        // 카메라 위치 + 회전을 반영한 상대위치로 월드위치 계산
        Vector3 worldPosition = cameraPosition + cameraRotation * yInversionPos;

        // --- 회전 ---
        using Mat rvecMat = new Mat(3, 1, MatType.CV_64FC1);

        rvecMat.Set(0, 0, result.rvec[0]);
        rvecMat.Set(1, 0, result.rvec[1]);
        rvecMat.Set(2, 0, result.rvec[2]);

        using Mat rotationMatrix = new Mat();
        Cv2.Rodrigues(rvecMat, rotationMatrix);

        // quaternion용 44행렬 생성
        Matrix4x4 m = Matrix4x4.identity;

        // 매트릭스의 행렬 지정후 가져올 매트릭스의 타입과 행렬 지정해 가져와 float으로 형변환
        m.m00 = (float)rotationMatrix.At<double>(0, 0);
        m.m01 = (float)rotationMatrix.At<double>(0, 1);
        m.m02 = (float)rotationMatrix.At<double>(0, 2);

        m.m10 = (float)rotationMatrix.At<double>(1, 0);
        m.m11 = (float)rotationMatrix.At<double>(1, 1);
        m.m12 = (float)rotationMatrix.At<double>(1, 2);

        m.m20 = (float)rotationMatrix.At<double>(2, 0);
        m.m21 = (float)rotationMatrix.At<double>(2, 1);
        m.m22 = (float)rotationMatrix.At<double>(2, 2);

        Matrix4x4 flipY = Matrix4x4.Scale(new Vector3(1f, -1f, 1f));

        // opencv와 유니티 회전의 방향차이를 맞추기 위해 S * R * S로 좌표계 변환
        m = flipY * m * flipY;

        // 카메라의 월드 회전까지 곱해줘 회전 맞추기
        Quaternion cameraWorldRotation = result.cameraRotation * m.rotation;

        // 마커 회전인 135도 보정
        Quaternion markerOffset = Quaternion.Euler(0f, 0f, 135f);

        // 최종 회전
        Quaternion worldRotation = cameraWorldRotation * markerOffset;

        return (worldPosition, worldRotation);
    }

    private bool isEnemyField(Vector3 worldPosition)
    {
        // 중심 기준으로 좌표 변경
        Vector3 localPosition = fieldRoot.transform.InverseTransformPoint(worldPosition);
        // 해당 축 기준으로 중앙 너머인지 여부 전달
        return localPosition.z > 0f;
    }

    // 원하는 id의 마커 정보 반환
    public bool TryGetTargetMarkerResult(int targetId, out MarkerPositionResult markerPositionResults)
    {
        // 아직 생성된 마커 정보가 없으면 null 반환
        if (this.markerPositionResults == null || this.markerPositionResults.Count == 0)
        {
            markerPositionResults = null;
            return false;
        }

        var targetMarker = this.markerPositionResults.Find(marker => marker.id == targetId);
        if (targetMarker != null)
        {
            markerPositionResults = targetMarker;
            return true;
        }
        else
        {
            markerPositionResults = null;
            return false;
        }
    }
    // 모든 마커 정보 반환
    public bool TryGetMarkerResult(out List<MarkerPositionResult> markerPositionResults)
    {
        if (this.markerPositionResults != null && this.markerPositionResults.Count > 0)
        {
            markerPositionResults = this.markerPositionResults;
            return true;
        }
        else
        {
            markerPositionResults = null;
            return false;
        }
    }
}
using UnityEngine;
using OpenCvSharp;
using System.Collections.Generic;

public class TrackingMarker : MonoBehaviour
{
    // 디버깅용 텍스트 ui
    // [SerializeField] private TMPro.TextMeshProUGUI debugText1;
    [SerializeField] private TMPro.TextMeshProUGUI debugText2;
    [SerializeField] private TMPro.TextMeshProUGUI debugText3;
    // 움직일 모델 앵커
    [SerializeField] private GameObject markerAnchor;
    // 탐지 코드 연결
    [SerializeField] private MarkerDetecter markerDetecter;
    // 손 속도 측정 코드
    [SerializeField] private HandManager handManager;
    // 데드존 설정값
    private float positionDeadZone = 0.001f;
    private float rotationDeadZone = 1.0f;

    // 앵커 월드기준 위치 저장용 변수
    private Vector3 worldPosition = Vector3.zero;
    private Quaternion worldRotation = Quaternion.identity;
    private Vector3 cameraPosition;
    private Quaternion cameraRotation;
    
    // 데드존용 좌표 변수
    private Vector3 deadZonePosition;
    private Quaternion deadZoneRotation;
    private bool poseInitialized = false;

    // 손 속도 보간용 변수
    // 손 속도 최소, 최대 기준
    private float minHandSpeed = 0.003f;
    private float maxHandSpeed = 0.8f;
    // 따라갈 속도 최대 최소 비율
    private float minFollowSpeed = 2f;
    private float maxFollowSpeed = 90f;

    // 이동 여부 변수
    private bool isHandMove = false;
    private bool isCardMove = false;
    private float maxSpeed = 0f;

    // 이번 마커들 저장용 리스트
    private List<MarkerPositionResult> markerPositionResults;

    // 새 마커 리스트 생성 여부
    private bool isNewMarkerList = false;

    // 목표 id
    [SerializeField] private int targetId = 9;

    void Update()
    {
        if(markerDetecter.TryGetMarkerResult(out var result))
        {
            // 마커들 저장용 리스트 초기화
            markerPositionResults = new List<MarkerPositionResult>(result.Count);

            // 마커 결과 리스트 순회하며 월드 좌표계로 변환 후 저장
            for(int i = 0; i < result.Count; i++)
            {
                // 첫번째 결과에서 카메라 좌표와 회전값을 가져와 저장
                if(i == 0)
                {
                    cameraPosition = result[i].cameraPosition;
                    cameraRotation = result[i].cameraRotation;
                }
                // 좌표계 변환 함수로 월드 좌표계로 변환
                (var markerWorldPosition, var markerWorldRotation) = LocalToWroldPos(result[i]);

                // 변환한 좌표를 id와 함께 리스트에 저장
                markerPositionResults.Add(new MarkerPositionResult{id = result[i].id, worldPosition = markerWorldPosition, worldRotation = markerWorldRotation});
            }
            // 목표 id와 일치하는 마커를 찾아 저장
            var targetMarker = markerPositionResults.Find(marker => marker.id == targetId);
            // 목표 마커가 있다면 월드 좌표와 회전값을 저장
            if (targetMarker != null)
            {
                worldPosition = targetMarker.worldPosition;
                worldRotation = targetMarker.worldRotation;
            }
        }
        // 손 속도를 활용해 보간값으로 활용
        float followSpeed = GetCardFollowSpeed(handManager.GetHandSpeed());
        // 손이 이동중이면 속도 기록
        if (isHandMove)
        {
            isCardMove = true;
            maxSpeed = Mathf.Max(maxSpeed, followSpeed);
        }
        // 현재 카드와 앵커의 거리와 각도를 비교해 2cm이상 또는 10도 이상 차이이면 이전의 최대속도로 이동
        float cardToMarkerDistance = Vector3.Distance(markerAnchor.transform.position, worldPosition);
        float cardToMarkerAngle = Quaternion.Angle(markerAnchor.transform.rotation, worldRotation);
        if(isCardMove && !isHandMove)
        {
            if (cardToMarkerDistance > 0.01f || cardToMarkerAngle > 7f)
            {
                followSpeed = maxSpeed;
            }
            else
            {
                isCardMove = false;
                maxSpeed = 0f;
            }
        }
        // 적용한 속도를 사용해 보간에 사용할 값으로 변환
        float t = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);

        // 이동 여부 판정 함수로 이동여부 bool 받기
        var (applyPosition, applyRotation) = DeadZoneLimit();
        if (applyPosition)
        {
            // 보간으로 부드럽게 움직이도록 구성
            markerAnchor.transform.position = Vector3.Lerp(markerAnchor.transform.position, worldPosition, t);
        }
        if (applyRotation)
        {
            markerAnchor.transform.rotation = Quaternion.Slerp(markerAnchor.transform.rotation, worldRotation, t);
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

    private (bool applyPosition, bool applyRotation) DeadZoneLimit()
    {
        bool applyPosition = true;
        bool applyRotation = true;
        // 데드존 기준 위치 초기화
        if (!poseInitialized)
        {
            deadZonePosition = worldPosition;
            deadZoneRotation = worldRotation;
            poseInitialized = true;
        }
        else
        {
            // 이전 측정 마커와 현제 위치 비교
            float positionDelta = Vector3.Distance(deadZonePosition, worldPosition);
            float rotationDelta = Quaternion.Angle(deadZoneRotation, worldRotation);

            // 데드존 기준값과 비교 후 작을시 이동하지 않음
            if (positionDelta <= positionDeadZone)
            {
                applyPosition = false;
            }
            else
            {
                deadZonePosition = worldPosition;
            }

            if (rotationDelta <= rotationDeadZone || rotationDelta >= 150)
            {
                applyRotation = false;
            }
            else
            {
                deadZoneRotation = worldRotation;
            }
        }
        return (applyPosition, applyRotation);
    }

    private float GetCardFollowSpeed(float handSpeed)
    {
        // 현재 손 속도를 최소, 최대값과 비교해 0~1값으로 출력
        float t = Mathf.InverseLerp(minHandSpeed, maxHandSpeed, handSpeed);
        if (t > 0.3)
        {
            isHandMove = true;
        }
        else
        {
            isHandMove = false;
        }
        // 앞서 나온 0~1값으로 최소, 최대 사이 비율을 맞춰 값 반환
        return Mathf.Lerp(minFollowSpeed, maxFollowSpeed, t);
    }

    public bool TryGetTargetMarkerResult(int targetId, out MarkerPositionResult markerPositionResults)
    {
        var targetMarker = this.markerPositionResults.Find(marker => marker.id == targetId);
        if (this.markerPositionResults != null && this.markerPositionResults.Count > 0 && targetMarker != null)
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
}
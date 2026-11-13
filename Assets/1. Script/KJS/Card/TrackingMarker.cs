using UnityEngine;

public class TrackingMarker : MonoBehaviour
{
    // 손 속도 측정 코드
    [SerializeField] private HandManager handManager;
    // 마커 월드 좌표 생성 코드 받기
    [SerializeField] private MarkerWorldPos markerWorldPos;
    // 자세히보기 모드 여부 받기
    [SerializeField] private DetailModeManager detailModeManager;

    // 데드존 설정값
    private float positionDeadZone = 0.001f;
    private float rotationDeadZone = 1.0f;

    // 앵커 월드기준 위치 저장용 변수
    private Vector3 worldPosition = Vector3.zero;
    private Quaternion worldRotation = Quaternion.identity;

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

    // 목표 id
    public int TargetCardId{get; private set;}

    // 자세히보기 설정
    private bool isDetaile = false;

    void Update()
    {
        // 목표 id와 일치하는 마커를 찾아 저장
        if(markerWorldPos.TryGetTargetCardResult(TargetCardId, out var targetMarker))
        {
            worldPosition = targetMarker.worldPosition;
            worldRotation = targetMarker.worldRotation;
            // 자세히 보기 모드일때 보간 사용
            if (detailModeManager.IsDetailMode && detailModeManager.LastMarkerId == TargetCardId)
            {
                // 손 속도를 활용해 보간값으로 활용
                float followSpeed = GetCardFollowSpeed(handManager.GetHandSpeed());
                // 손이 이동중이면 속도 기록
                if (isHandMove)
                {
                    isCardMove = true;
                    maxSpeed = Mathf.Max(maxSpeed, followSpeed);
                }
                // 현재 카드와 앵커의 거리와 각도를 비교해 기준이상 차이이면 이전의 최대속도로 이동
                float cardToMarkerDistance = Vector3.Distance(transform.position, worldPosition);
                float cardToMarkerAngle = Quaternion.Angle(transform.rotation, worldRotation);
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
                    transform.position = Vector3.Lerp(transform.position, worldPosition, t);
                }
                if (applyRotation)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, worldRotation, t);
                }
            }
            // 일반 상태일신 보간없이 바로 이동
            else
            {
                transform.position = worldPosition;
                transform.rotation = worldRotation;
            }
        }
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
    public void Initialize(int id, HandManager handManager, MarkerWorldPos markerWorldPos, DetailModeManager detailModeManager)
    {
        TargetCardId = id;
        this.handManager = handManager;
        this.markerWorldPos = markerWorldPos;
        this.detailModeManager = detailModeManager;
    }
    public void SetisDetaile(bool detaileMode)
    {
        isDetaile = detaileMode;
    }
}

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
        if (!markerWorldPos.TryGetTargetCardResult(TargetCardId, out var targetMarker)) return;
        worldPosition = targetMarker.worldPosition;
        worldRotation = targetMarker.worldRotation;
        if (detailModeManager != null && detailModeManager.DetailCardId == TargetCardId)
            FollowDetailPose(Time.deltaTime);
        else
            ResetPose(worldPosition, worldRotation);
    }

    private void FollowDetailPose(float deltaTime)
    {
        UpdateFollowTarget();
        float followSpeed = GetCardFollowSpeed(handManager.GetHandSpeed());
        if (isHandMove)
        {
            isCardMove = true;
            maxSpeed = Mathf.Max(maxSpeed, followSpeed);
        }
        else if (isCardMove)
        {
            if (Vector3.Distance(transform.position, deadZonePosition) > 0.01f ||
                Quaternion.Angle(transform.rotation, deadZoneRotation) > 7f)
            {
                followSpeed = maxSpeed;
            }
            else
            {
                isCardMove = false;
                maxSpeed = 0f;
            }
        }

        // Continue following the accepted pose between camera detections.
        float t = 1f - Mathf.Exp(-followSpeed * deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, deadZonePosition, t),
            Quaternion.Slerp(transform.rotation, deadZoneRotation, t));
    }

    private void UpdateFollowTarget()
    {
        if (!poseInitialized)
        {
            deadZonePosition = worldPosition;
            deadZoneRotation = worldRotation;
            poseInitialized = true;
            return;
        }

        if (Vector3.Distance(deadZonePosition, worldPosition) > positionDeadZone)
            deadZonePosition = worldPosition;
        float angle = Quaternion.Angle(deadZoneRotation, worldRotation);
        if (angle > rotationDeadZone && angle < 150f)
            deadZoneRotation = worldRotation;
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

    public void ResetPose(Vector3 position, Quaternion rotation)
    {
        worldPosition = position;
        worldRotation = rotation;
        poseInitialized = false;
        isHandMove = false;
        isCardMove = false;
        maxSpeed = 0f;
        transform.SetPositionAndRotation(position, rotation);
    }

    public void SetisDetaile(bool detaileMode)
    {
        isDetaile = detaileMode;
    }
}

using UnityEngine;

public class StartInit : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI debug1;
    [SerializeField] private TMPro.TextMeshProUGUI debug3;
    [SerializeField] private TrackingMarker trackingMarker;
    [SerializeField] private AnchorManager anchorManager;

    // 앵커 생성 여부
    private bool isMiddleAnchor = false;
    // 앵커 기준 id
    private int middleAnchorId = 9;

    // 앵커 생성용 보간 좌표
    private Vector3 lerpPosition;
    private Quaternion slerpRotation;
    // 좌표 초기화 여부
    private bool hasInitPos = false;
    // 좌표 보간용 값
    private float t = 5;
    // 좌표 기준 대기값
    private float anchorReadyTime = 0;
    private float anchorCreateTime = 2f;

    void Start()
    {
        debug1.text = "Set midle Anchor";
        
    }

    // Update is called once per frame
    void Update()
    {
        debug3.text = $"anchorTime: {anchorReadyTime}";
        if (!isMiddleAnchor)
        {
            if(trackingMarker.TryGetTargetMarkerResult(middleAnchorId, out var markerPositionResult))
            {
                Vector3 newPosition = markerPositionResult.worldPosition;
                Quaternion newRotation = markerPositionResult.worldRotation;
                if (!hasInitPos)
                {
                    lerpPosition = newPosition;
                    slerpRotation = newRotation;
                    hasInitPos = true;
                    return;
                }
                lerpPosition = Vector3.Lerp(lerpPosition, newPosition, t * Time.deltaTime);
                slerpRotation = Quaternion.Slerp(slerpRotation, newRotation, (t - 2) * Time.deltaTime);
                anchorReadyTime += Time.deltaTime;
            }
            else
            {
                anchorReadyTime = 0f;
                hasInitPos = false;
            }
            if(anchorReadyTime >= anchorCreateTime)
            {
                isMiddleAnchor = true;
                anchorManager.CreateTableAnchor(lerpPosition, slerpRotation);
                debug1.text = "Middle Anchor Setting Complete";
            }
        }
        
    }
}

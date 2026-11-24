using UnityEngine;

public class StartInit : MonoBehaviour
{
    [SerializeField] private TMPro.TextMeshProUGUI debug3;
    [SerializeField] private MarkerWorldPos markerWorldPos;
    [SerializeField] private AnchorManager anchorManager;
    [SerializeField] private RectTransform markerArrow;
    [SerializeField] private UnityEngine.UI.Image markerArrowInside;
    [SerializeField] private Transform viewer;
    [SerializeField, Min(0f)] private float markerDelay = 0.5f;
    [SerializeField, Min(0.01f)] private float anchorCreateTime = 3.5f;
    [SerializeField] private float arrowHeight = 0.045f;
    [SerializeField, Min(0f)] private float bobHeight = 0.006f;

    private const float FrameTimeout = 1f;
    private uint lastFrameVersion;
    private float seenSince = -1f;
    private float lastSeen;
    private Vector3 markerPosition;
    private Quaternion markerRotation;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool initialized;
    private bool placeNextFrame;

    void LateUpdate() => UpdatePlacement(Time.unscaledTime, Time.unscaledDeltaTime);

    private void UpdatePlacement(float now, float delta)
    {
        if (anchorManager.IsReady) return;
        if (!initialized)
        {
            initialized = true;
            // 슬롯이 다른 컴포넌트에 먼저 등록되도록 씬의 Start가 끝난 뒤 숨긴다.
            if (markerArrow != null) anchorManager.SetFieldVisible(false);
        }
        if (placeNextFrame)
        {
            if (markerArrow != null) markerArrow.gameObject.SetActive(false);
            anchorManager.CreateTableAnchor(markerPosition, markerRotation);
            if (markerArrow != null) anchorManager.SetFieldVisible(true);
            return;
        }

        bool newFrame = lastFrameVersion != markerWorldPos.FrameVersion;
        if (newFrame)
        {
            lastFrameVersion = markerWorldPos.FrameVersion;
            MarkerPositionResult observation = null;
            foreach (var marker in markerWorldPos.FrameMarkers)
            {
                if (marker.markerId != 0) continue;
                observation = marker;
                break;
            }
            if (observation == null)
            {
                ResetProgress();
                return;
            }

            targetPosition = observation.worldPosition;
            targetRotation = observation.worldRotation;
            if (seenSince < 0f || now - lastSeen > FrameTimeout)
            {
                seenSince = now;
                markerPosition = targetPosition;
                markerRotation = targetRotation;
            }
            lastSeen = now;
        }

        if (seenSince < 0f) return;
        if (now - lastSeen > FrameTimeout)
        {
            ResetProgress();
            return;
        }

        markerPosition = Vector3.Lerp(markerPosition, targetPosition, 5f * delta);
        markerRotation = Quaternion.Slerp(markerRotation, targetRotation, 3f * delta);
        float elapsed = Mathf.Max(0f, now - seenSince - (markerArrow != null ? markerDelay : 0f));
        float progress = Mathf.Clamp01(elapsed / anchorCreateTime);
        placeNextFrame = newFrame && progress >= 1f;
        if (debug3 != null) debug3.SetText("anchorTime: {0:F1}", elapsed);
        if (markerArrow == null) return;

        float bob = Mathf.Sin((now - seenSince) * 5f) * bobHeight;
        markerArrow.position = markerPosition + Vector3.up * (arrowHeight + bob);
        if (viewer != null)
        {
            var forward = markerArrow.position - viewer.position;
            if (forward.sqrMagnitude > 0.0001f)
                markerArrow.rotation = Quaternion.LookRotation(forward, viewer.up);
        }
        markerArrowInside.fillAmount = placeNextFrame ? 1f : Mathf.Min(progress, 0.99f);
        markerArrow.gameObject.SetActive(true);
    }

    private void ResetProgress()
    {
        seenSince = -1f;
        if (debug3 != null) debug3.text = "anchorTime: 0";
        if (markerArrow == null) return;
        markerArrowInside.fillAmount = 0f;
        markerArrow.gameObject.SetActive(false);
    }
}

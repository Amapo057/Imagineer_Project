using System.Collections.Generic;
using UnityEngine;

public class CardColliderManager : MonoBehaviour
{
    [SerializeField] private MarkerWorldPos markerWorldPos;
    [SerializeField] private GameObject cardColliderPrefab;
    [SerializeField] private HandManager handManager;
    [SerializeField] private DetailModeManager detailModeManager;

    private const float TrackingTimeout = 3f;

    private sealed class TrackedCard
    {
        public TrackingMarker marker;
        public float lastSeen;
    }

    private readonly Dictionary<int, TrackedCard> cardInstances = new();
    private uint lastFrameVersion;

    void Update() => UpdateTracking(Time.unscaledTime);

    private void LateUpdate()
    {
        if (detailModeManager == null) return;
        Transform anchor = null;
        if (cardInstances.TryGetValue(detailModeManager.DetailCardId, out var card) && card.marker.isActiveAndEnabled)
            anchor = card.marker.transform;
        detailModeManager.ShowDetailOn(anchor);
    }

    private void OnDisable()
    {
        if (detailModeManager != null) detailModeManager.ShowDetailOn(null);
    }

    private void UpdateTracking(float now)
    {
        if (lastFrameVersion != markerWorldPos.FrameVersion)
        {
            lastFrameVersion = markerWorldPos.FrameVersion;
            foreach (var observation in markerWorldPos.FrameMarkers)
            {
                if (observation.cardId <= 0) continue;
                if (!cardInstances.TryGetValue(observation.cardId, out var card))
                {
                    var obj = Instantiate(cardColliderPrefab, observation.worldPosition, observation.worldRotation);
                    var tracking = obj.GetComponent<TrackingMarker>();
                    tracking.Initialize(observation.cardId, handManager, markerWorldPos, detailModeManager);
                    card = new TrackedCard { marker = tracking };
                    cardInstances.Add(observation.cardId, card);
                }

                card.lastSeen = now;
                if (!card.marker.gameObject.activeSelf)
                {
                    card.marker.ResetPose(observation.worldPosition, observation.worldRotation);
                    card.marker.gameObject.SetActive(true);
                }
            }
        }

        foreach (var card in cardInstances.Values)
            if (card.marker.gameObject.activeSelf && now - card.lastSeen >= TrackingTimeout)
                card.marker.gameObject.SetActive(false);
    }
}

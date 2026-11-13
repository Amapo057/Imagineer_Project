using UnityEngine;

// Replaces the legacy slot only in the production scene copy. A model follows board state,
// never an unaccepted marker. Removing a physical card does not undo a played card.
public sealed class ProductionCardSlot : MonoBehaviour
{
    public ProductionGameController session;
    public MarkerWorldPos marker;
    public int lane;
    public float modelScale = 0.1f;
    int candidate = -1;
    uint version;
    CardInstance displayed;
    GameObject model;
    GameManager previous;
    bool blockedUntilRemoved;
    void Update()
    {
        if (previous != session.Game)
        { previous = session.Game; candidate = -1; blockedUntilRemoved = true; }
        var card = session.TableFeedback != null
            ? session.TableFeedback.DisplayedCard(PlayerSide.Me, lane) : session.Game.Board.me.lanes[lane];
        if (displayed == card) return;
        displayed = card;
        if (model != null) Destroy(model);
        if (card == null) return;
        if (card.data.hologramPrefab == null) { model = ProductionCardLabel.Create(card, transform, lane); return; }
        model = Instantiate(card.data.hologramPrefab, transform);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        var p = transform.lossyScale; var s = model.transform.localScale * modelScale;
        model.transform.localScale = new Vector3(s.x / p.x, s.y / p.y, s.z / p.z);
    }
    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Card") || !other.TryGetComponent<TrackingMarker>(out var card)) return;
        blockedUntilRemoved = false; candidate = card.TargetCardId; version = marker.MarkerVersion;
    }
    void OnTriggerStay(Collider other)
    {
        if (blockedUntilRemoved || !other.CompareTag("Card") || !other.TryGetComponent<TrackingMarker>(out var card)) return;
        if (candidate != card.TargetCardId) { candidate = card.TargetCardId; version = marker.MarkerVersion; return; }
        if (version == marker.MarkerVersion) return;
        version = marker.MarkerVersion;
        if (!marker.TryGetTargetCardResult(candidate, out var detection) || detection.isBack) return;
        if (session.TryPlacePhysicalCard(candidate, lane)) blockedUntilRemoved = true;
    }
    void OnTriggerExit(Collider other)
    {
        if (!other.TryGetComponent<TrackingMarker>(out var card) || card.TargetCardId != candidate) return;
        candidate = -1; blockedUntilRemoved = false;
    }
}


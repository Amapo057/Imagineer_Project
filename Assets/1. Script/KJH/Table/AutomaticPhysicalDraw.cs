using UnityEngine;

// Deck-back -> positively observed outside the deck -> existing networked confirmation.
public sealed class AutomaticPhysicalDraw : MonoBehaviour
{
    public ProductionGameController session;
    public MarkerWorldPos marker;
    public BoxCollider deck;
    public bool IsConfigured => enabled && marker != null && deck != null;
    readonly PhysicalDrawTracker tracker = new PhysicalDrawTracker();
    GameManager previous;
    uint version;

    void Start()
    {
        if (session == null) session = GetComponent<ProductionGameController>();
        if (marker == null) marker = FindFirstObjectByType<MarkerWorldPos>();
        if (deck == null)
        {
            var slot = FindFirstObjectByType<DeckSlot>();
            if (slot != null) deck = slot.GetComponent<BoxCollider>();
        }
    }
    void Update()
    {
        if (!IsConfigured || session == null) return;
        if (previous != session.Game) { previous = session.Game; tracker.Reset(); version = marker.MarkerVersion; }
        if (!session.HardwareReady || !session.Ready || !session.Network.Connected || session.Game.IsGameOver ||
            VRGameSession.PendingPhysicalDraws(PlayerSide.Me) <= 0) return;
        if (version == marker.MarkerVersion) return;
        version = marker.MarkerVersion;
        if (!marker.TryGetMarkerResult(out var observations)) return;
        var inner = new Bounds(deck.center, deck.size + Vector3.one * 0.03f);
        var outer = new Bounds(deck.center, deck.size + Vector3.one * 0.10f);
        foreach (var card in observations)
        {
            if (session.Database.GetCardById(card.cardId) == null) continue;
            var point = deck.transform.InverseTransformPoint(card.worldPosition);
            if (!tracker.Observe(card.cardId, card.isBack, inner.Contains(point), !outer.Contains(point), Time.unscaledTime)) continue;
            int before = VRGameSession.ConfirmedPhysicalDraws(PlayerSide.Me);
            session.ConfirmDraw();
            if (VRGameSession.ConfirmedPhysicalDraws(PlayerSide.Me) > before) tracker.Confirm(card.cardId);
            if (VRGameSession.PendingPhysicalDraws(PlayerSide.Me) <= 0 || session.Game.IsGameOver) break;
        }
    }
}

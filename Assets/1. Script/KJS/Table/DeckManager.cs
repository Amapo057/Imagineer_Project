using System.Collections.Generic;
using Oculus.Interaction.Input;
using UnityEngine;

public sealed class DeckManager : MonoBehaviour
{
    [SerializeField] private BoxCollider deckSlot;
    [SerializeField] private MarkerWorldPos marker;
    [SerializeField] private Hand leftHand;
    [SerializeField] private Hand rightHand;

    // 한 플레이어의 덱에는 같은 카드가 한 장만 있다. 앞뒷면은 동일한 cardId를 사용한다.
    private readonly HashSet<int> registeredCards = new();
    private readonly HashSet<int> drawnCards = new();
    private readonly HashSet<int> pendingCards = new();
    private GameManager match;
    private uint frameVersion;

    public int RegisteredCount
    {
        get { BindMatch(VRGameSession.Game); return registeredCards.Count; }
    }
    public int RemainingCount => VRGameSession.RemainingPhysicalDeck(PlayerSide.Me);

    void Update()
    {
        var flow = ProductionGameController.Current;
        if (flow == null || !flow.Ready || !flow.HardwareReady || flow.Game.IsGameOver ||
            !VRGameSession.NetworkPlayAllowed || deckSlot == null || marker == null) return;
        BindMatch(flow.Game);
        ConfirmPendingCards();
        if (frameVersion == marker.FrameVersion) return;
        frameVersion = marker.FrameVersion;

        var bounds = new Bounds(deckSlot.center, deckSlot.size);
        var departureBounds = bounds;
        departureBounds.Expand(0.04f);
        foreach (var card in marker.FrameMarkers)
        {
            if (card.cardId <= 0) continue;
            var point = deckSlot.transform.InverseTransformPoint(card.worldPosition);
            bool inside = bounds.Contains(point);
            if (flow.Database.GetCardById(card.cardId) == null) continue;
            if (inside)
            {
                registeredCards.Add(card.cardId);
            }
            else if (!departureBounds.Contains(point) &&
                     (registeredCards.Contains(card.cardId) || IsNearHand(card.worldPosition)))
            {
                RecordDrawnCard(card.cardId, confirmHand: true);
            }
        }
    }

    public void RecordPlayedCard(int cardId)
    {
        BindMatch(VRGameSession.Game);
        RecordDrawnCard(cardId, confirmHand: false);
    }

    void RecordDrawnCard(int cardId, bool confirmHand)
    {
        registeredCards.Add(cardId);
        if (drawnCards.Contains(cardId)) return;
        if (confirmHand)
        {
            pendingCards.Add(cardId);
            ConfirmPendingCards();
            return;
        }
        pendingCards.Remove(cardId);
        if (!drawnCards.Add(cardId)) return;
        // A missing marker may mean the deck moved or is covered. Count only identified cards.
        VRGameSession.SetPhysicalDeckRemaining(PlayerSide.Me, GameRules.DeckSize - drawnCards.Count);
    }

    void ConfirmPendingCards()
    {
        while (pendingCards.Count > 0)
        {
            if (MultiplayerManager.Current == null || !MultiplayerManager.Current.TryConfirmDraw()) return;
            int cardId = 0;
            foreach (int pending in pendingCards) { cardId = pending; break; }
            RecordDrawnCard(cardId, confirmHand: false);
        }
    }

    void BindMatch(GameManager game)
    {
        if (match == game) return;
        match = game;
        registeredCards.Clear();
        drawnCards.Clear();
        pendingCards.Clear();
        frameVersion = marker != null ? marker.FrameVersion : 0;
    }

    bool IsNearHand(Vector3 position) => IsNearHand(leftHand, position) || IsNearHand(rightHand, position);

    static bool IsNearHand(Hand hand, Vector3 position) =>
        hand != null && hand.GetJointPose(HandJointId.HandMiddle2, out Pose pose) &&
        (pose.position - position).sqrMagnitude <= 0.12f * 0.12f;
}

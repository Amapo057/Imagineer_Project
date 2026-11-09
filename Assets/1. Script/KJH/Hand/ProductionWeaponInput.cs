using UnityEngine;

// Existing fist gesture + physical entry into an enemy slot; no gaze attack buttons.
public sealed class ProductionWeaponInput : MonoBehaviour
{
    ProductionGameController session;
    WeaponManager hand;
    ProductionOpponentView[] slots;
    bool active;
    int previousLane = -1;
    void Start()
    {
        session = GetComponent<ProductionGameController>();
        hand = FindFirstObjectByType<WeaponManager>();
        slots = FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None);
    }
    public static bool Contains(Transform slot, Vector3 position)
    {
        Vector3 d = position - slot.position;
        float height = Vector3.Dot(d, slot.up);
        return Mathf.Abs(Vector3.Dot(d, slot.right)) <= 0.075f &&
            Mathf.Abs(Vector3.Dot(d, slot.forward)) <= 0.09f && height >= 0.02f && height <= 0.25f;
    }
    void LateUpdate()
    {
        if (session == null || hand == null || !session.CanAct || session.PendingWeapons == 0 || !hand.TryGetStrikePosition(out Vector3 position))
        { active = false; previousLane = -1; return; }
        int target = -1;
        foreach (var slot in slots)
            if (session.Game.Board.opponent.lanes[slot.lane] != null && Contains(slot.transform, position)) { target = slot.lane; break; }
        // Arming/closing a fist already over a target cannot cause an accidental hit.
        if (active && previousLane == -1 && target >= 0) session.TryStrikeWeapon(target);
        active = true; previousLane = target;
    }
}

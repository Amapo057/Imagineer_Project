using UnityEngine;

public sealed class WeaponAnchor : MonoBehaviour
{
    [SerializeField] private WeaponManager weaponManager;

    private readonly Collider[] contacts = new Collider[64];
    private int previousTarget = -1;
    private int previousWeapon = -1;

    void LateUpdate()
    {
        var session = ProductionGameController.Current;
        int source = session != null ? session.EquippedWeaponCardId : -1;
        bool tracked = weaponManager.TryGetPalmPose(out var palm);
        if (tracked) transform.SetPositionAndRotation(palm.position, palm.rotation);

        bool visible = source > 0 && tracked && weaponManager.IsHeld && session.CanAct;
        if (!weaponManager.ShowEquippedWeapon(source, visible))
        {
            previousTarget = previousWeapon = -1;
            return;
        }

        int target = FindTarget(weaponManager.EquippedHitbox);
        // 주먹을 쥘 때 이미 겹쳐 있으면, 무기를 뺐다가 다시 닿아야 한다.
        if (source == previousWeapon && previousTarget < 0 && target >= 0 && session.TryStrikeWeapon(target))
        {
            weaponManager.ShowEquippedWeapon(session.EquippedWeaponCardId, false);
            previousTarget = previousWeapon = -1;
            return;
        }
        previousTarget = target;
        previousWeapon = source;
    }

    private int FindTarget(BoxCollider hitbox)
    {
        if (hitbox == null || !hitbox.enabled || !hitbox.gameObject.activeInHierarchy) return -1;

        var hitboxTransform = hitbox.transform;
        var scale = hitboxTransform.lossyScale;
        var halfExtents = Vector3.Scale(hitbox.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        var center = hitboxTransform.TransformPoint(hitbox.center);
        int count = Physics.OverlapBoxNonAlloc(center, halfExtents, contacts,
            hitboxTransform.rotation, Physics.AllLayers, QueryTriggerInteraction.Collide);
        int target = -1;
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var contact = contacts[i];
            var view = contact.GetComponentInParent<ProductionOpponentView>();
            if (view == null || !view.IsModelCollider(contact)) continue;
            float distance = (contact.ClosestPoint(center) - center).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance;
            target = view.lane;
        }
        return target;
    }
}

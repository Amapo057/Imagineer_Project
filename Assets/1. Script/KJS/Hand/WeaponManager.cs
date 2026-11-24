using Oculus.Interaction.Input;
using UnityEngine;

public class WeaponManager : MonoBehaviour
{
    [System.Serializable]
    private struct WeaponPrefab
    {
        public CardData card;
        public GameObject prefab;
    }

    [SerializeField] private Hand hand;
    [SerializeField] private Transform weaponAnchor;
    [SerializeField] private WeaponPrefab[] weaponPrefabs;
    [SerializeField] private GameObject weaponModel;
    private WeaponAnchor combatAnchor;
    private GameObject equippedModel;
    private int equippedCardId = -1;
    bool held;

    internal bool IsHeld => held;
    internal BoxCollider EquippedHitbox { get; private set; }

    void Awake()
    {
        if (weaponAnchor != null) combatAnchor = weaponAnchor.GetComponent<WeaponAnchor>();
    }

    internal bool TryGetPalmPose(out Pose pose)
    {
        pose = default;
        return hand != null && hand.GetJointPose(HandJointId.HandPalm, out pose);
    }

    internal GameObject GetWeaponPrefab(int cardId)
    {
        if (cardId <= 0 || weaponPrefabs == null) return null;
        foreach (var entry in weaponPrefabs)
            if (entry.card != null && entry.card.cardId == cardId && entry.prefab != null)
                return entry.prefab;
        return null;
    }

    internal bool TryGetVisibleWeapon(out int cardId, out Pose pose)
    {
        cardId = 0;
        pose = default;
        if (equippedModel == null || !equippedModel.activeInHierarchy) return false;
        cardId = equippedCardId;
        pose = new Pose(weaponAnchor.position, weaponAnchor.rotation);
        return true;
    }

    internal bool ShowEquippedWeapon(int cardId, bool visible)
    {
        if (equippedCardId != cardId)
        {
            equippedCardId = cardId;
            if (equippedModel != null)
            {
                equippedModel.SetActive(false);
                Destroy(equippedModel);
            }
            equippedModel = null;
            EquippedHitbox = null;
            var prefab = GetWeaponPrefab(cardId);
            if (prefab != null)
            {
                equippedModel = Instantiate(prefab, weaponAnchor, false);
                EquippedHitbox = equippedModel.transform.Find("Collider")?.GetComponent<BoxCollider>();
                if (EquippedHitbox != null) EquippedHitbox.isTrigger = true;
            }
        }

        if (equippedModel == null) return false;
        if (equippedModel.activeSelf != visible) equippedModel.SetActive(visible);
        return visible;
    }

    public bool TryGetStrikePosition(out Vector3 position)
    {
        position = default;
        if (!held || !TryGetPalmPose(out var palm)) return false;
        position = palm.position; return true;
    }

    private void Update()
    {
        if (combatAnchor != null || hand == null) return;
        // 인터렉티브  sdk를 사용하니 해당 sdk의 GetJointPose함수를 사용해 손 정보 받기
        if(hand.GetJointPose(HandJointId.HandPalm, out Pose palmPose))
        {
            weaponAnchor.position = palmPose.position;
            weaponAnchor.rotation = palmPose.rotation;
        } 
        var game = ProductionGameController.Current;
        if (game != null && weaponModel != null)
            weaponModel.SetActive(held && game.CanAct && game.PendingWeapons > 0 && TryGetStrikePosition(out _));
    }

    public void RockDetected()
    {
        // 주먹쥐면 무기용 모델 활성화
        held = true;
        if (combatAnchor != null || weaponModel == null) return;
        var game = ProductionGameController.Current;
        weaponModel.SetActive(game == null || (game.CanAct && game.PendingWeapons > 0));
    }
    public void RockReleased()
    {
        held = false;
        if (combatAnchor == null && weaponModel != null) weaponModel.SetActive(false);
    }
}

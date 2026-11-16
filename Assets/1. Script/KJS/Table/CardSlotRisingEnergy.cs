using TMPro;
using UnityEngine;

public class CardSlotRisingEnergy : MonoBehaviour
{
    [SerializeField] private GameObject energyWall;
    [SerializeField] private PlayerSide side;
    [SerializeField, Range(0, 3)] private int lane;
    [SerializeField] private TMP_Text attackText;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private GameObject attackBackground;
    [SerializeField] private GameObject healthBackground;
    [SerializeField] private Animator statsAnimator;

    private bool wasActive;

    private void Update()
    {
        var lanes = VRGameSession.Game.Board.GetBoard(side).lanes;
        var card = lane >= 0 && lane < lanes.Length ? lanes[lane] : null;
        bool active = card != null;
        if (energyWall.activeSelf != active) energyWall.SetActive(active);
        SetVisible(attackText ? attackText.gameObject : null, active);
        SetVisible(healthText ? healthText.gameObject : null, active);
        SetVisible(attackBackground, active);
        SetVisible(healthBackground, active);
        if (active && !wasActive && statsAnimator) statsAnimator.Play("CardSlot_StatsReveal", 0, 0f);
        wasActive = active;
        if (!active) return;
        if (attackText) attackText.SetText("{0}", card.currentAttack);
        if (healthText) healthText.SetText("{0}", card.currentHealth);
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target && target.activeSelf != visible) target.SetActive(visible);
    }
}

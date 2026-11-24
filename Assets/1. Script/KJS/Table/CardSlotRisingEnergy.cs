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
    private float defaultStatsHeight;

    private void Awake()
    {
        if (statsAnimator != null) defaultStatsHeight = statsAnimator.transform.localPosition.y;
    }

    public void SetStatsHeight(Transform model)
    {
        if (statsAnimator == null) return;
        var stats = statsAnimator.transform;
        var position = stats.localPosition;
        var anchor = model != null ? model.Find("UIAnchor") : null;
        position.y = defaultStatsHeight;
        // 원점에 남은 UIAnchor는 아직 높이를 설정하지 않은 상태다.
        if (anchor != null && !Mathf.Approximately(anchor.localPosition.y, 0f))
            position.y = stats.parent.InverseTransformPoint(anchor.position).y;
        stats.localPosition = position;
    }

    private void Update()
    {
        var lanes = VRGameSession.Game.Board.GetBoard(side).lanes;
        var card = lane >= 0 && lane < lanes.Length ? lanes[lane] : null;
        var feedback = ProductionGameController.Current?.TableFeedback;
        if (feedback != null) card = feedback.DisplayedCard(side, lane);
        bool active = card != null;
        if (energyWall.activeSelf != active) energyWall.SetActive(active);
        SetVisible(attackText ? attackText.gameObject : null, active);
        SetVisible(healthText ? healthText.gameObject : null, active);
        SetVisible(attackBackground, active);
        SetVisible(healthBackground, active);
        if (active && !wasActive && statsAnimator) statsAnimator.Play("CardSlot_StatsReveal", 0, 0f);
        wasActive = active;
        if (!active) return;
        int attack = card.currentAttack, health = card.currentHealth;
        feedback?.DisplayedStats(side, lane, card, out attack, out health);
        if (attackText) attackText.SetText("{0}", attack);
        if (healthText) healthText.SetText("{0}", health);
    }

    private static void SetVisible(GameObject target, bool visible)
    {
        if (target && target.activeSelf != visible) target.SetActive(visible);
    }
}

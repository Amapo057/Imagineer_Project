using UnityEngine;

// Remote field is rendered from the synchronized board, in this headset's local table space.
public sealed class ProductionOpponentView : MonoBehaviour
{
    public int lane;
    public float modelScale = 0.1f;
    CardInstance displayed;
    GameObject model;
    void Update()
    {
        var feedback = ProductionGameController.Current?.TableFeedback;
        var card = feedback != null
            ? feedback.DisplayedCard(PlayerSide.Opponent, lane) : VRGameSession.Game.Board.opponent.lanes[lane];
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
}

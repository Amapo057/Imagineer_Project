using TMPro;
using UnityEngine;

// A readable field token when the card has no assigned character asset.
public sealed class ProductionCardLabel : MonoBehaviour
{
    public CardInstance card;
    TextMeshPro label;
    public static GameObject Create(CardInstance card, Transform parent)
    {
        var root = new GameObject("Card " + card.data.cardId);
        root.transform.SetParent(parent, false);
        root.transform.position = parent.position + parent.up * 0.1f;
        var scale = parent.lossyScale;
        root.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        var view = root.AddComponent<ProductionCardLabel>(); view.card = card;
        view.label = root.AddComponent<TextMeshPro>();
        view.label.font = Object.FindFirstObjectByType<ProductionGameUI>()?.font;
        view.label.outlineColor = Color.black; view.label.outlineWidth = 0.15f;
        view.label.fontSize = 0.18f; view.label.alignment = TextAlignmentOptions.Center;
        view.label.rectTransform.sizeDelta = new Vector2(0.24f, 0.12f);
        return root;
    }
    void LateUpdate()
    {
        label.text = card.data.cardName + "\n" + card.currentAttack + " / " + card.currentHealth;
        if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
    }
}

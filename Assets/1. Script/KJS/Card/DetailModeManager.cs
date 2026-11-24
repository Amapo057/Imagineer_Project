using TMPro;
using UnityEngine;

public class DetailModeManager : MonoBehaviour
{
    [SerializeField] private MarkerWorldPos markerWorldPos;
    [SerializeField] private TMP_Text debug2;
    [SerializeField] private GameObject detailPrefab;
    [SerializeField] private CardDatabase cardDatabase;
    [SerializeField] private CardArtwork[] artworkByCard;

    [System.Serializable]
    private struct CardArtwork
    {
        public Sprite illustration;
        public Sprite frontMarker;
    }

    private const float DetailRange = 0.3f;
    // The tracked ArUco plane points toward the camera and is a quarter turn from the card UI.
    private static readonly Quaternion MarkerToCard =
        Quaternion.AngleAxis(90f, Vector3.forward) * Quaternion.AngleAxis(180f, Vector3.right);
    private const int RequiredFrames = 4;
    private const float DescriptionFadeTime = 0.2f;
    private uint lastFrameVersion;
    private int candidateId = -1;
    private int consecutiveFrames;
    private float phaseStart;
    private float phaseDuration;
    private GameObject detailView;
    private UnityEngine.UI.RectMask2D revealMask;
    private CanvasGroup descriptionFade;
    private Transform displayedAnchor;
    private int displayedCardId = -1;

    public bool IsDetailMode => consecutiveFrames == RequiredFrames;
    public int DetailCardId { get; private set; } = -1;

    private void Update()
    {
        if (markerWorldPos == null || lastFrameVersion == markerWorldPos.FrameVersion) return;
        lastFrameVersion = markerWorldPos.FrameVersion;

        int nearestCard = -1;
        float nearestDistance = DetailRange * DetailRange;
        foreach (var marker in markerWorldPos.FrameMarkers)
        {
            if (marker.cardId <= 0 || (cardDatabase != null && cardDatabase.GetCardById(marker.cardId) == null)) continue;
            float distance = (marker.worldPosition - markerWorldPos.CameraPosition).sqrMagnitude;
            if (distance > nearestDistance) continue;
            nearestDistance = distance;
            nearestCard = marker.cardId;
        }

        UpdateCandidate(nearestCard);
        if (debug2 != null) debug2.text = IsDetailMode ? "detailMode" : $"Not Detail{consecutiveFrames}";
    }

    private void UpdateCandidate(int cardId)
    {
        if (cardId != candidateId || cardId <= 0)
        {
            candidateId = cardId;
            consecutiveFrames = cardId > 0 ? 1 : 0;
            phaseStart = Time.unscaledTime;
        }
        else if (consecutiveFrames < RequiredFrames)
        {
            phaseDuration = Mathf.Clamp(Time.unscaledTime - phaseStart, 0.04f, 0.5f);
            phaseStart = Time.unscaledTime;
            consecutiveFrames++;
        }
        DetailCardId = consecutiveFrames >= 2 ? cardId : -1;
    }

    public void ShowDetailOn(Transform anchor)
    {
        int cardId = DetailCardId;
        if (anchor == null || cardId <= 0 || detailPrefab == null || cardDatabase == null ||
            artworkByCard == null || cardId > artworkByCard.Length)
        {
            if (detailView != null) detailView.SetActive(false);
            displayedAnchor = null;
            displayedCardId = -1;
            return;
        }
        if (detailView != null && displayedAnchor == anchor && displayedCardId == cardId)
        {
            UpdateReveal();
            return;
        }

        var card = cardDatabase.GetCardById(cardId);
        if (card == null) return;
        if (detailView == null)
        {
            detailView = Instantiate(detailPrefab, anchor, false);
            revealMask = detailView.transform.Find("Canvas").gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            descriptionFade = detailView.transform.Find("Explane").gameObject.AddComponent<CanvasGroup>();
            descriptionFade.blocksRaycasts = false;
        }
        else detailView.transform.SetParent(anchor, false);

        var canvas = detailView.transform.Find("Canvas");
        var illustration = canvas.Find("Illust").GetComponent<UnityEngine.UI.Image>();
        var marker = canvas.Find("Marker").GetComponent<UnityEngine.UI.Image>();
        var artwork = artworkByCard[cardId - 1];
        illustration.sprite = artwork.illustration;
        marker.sprite = artwork.frontMarker;
        bool unit = card.cardType == CardType.Unit;
        canvas.Find("AT").GetComponent<TMP_Text>().text = unit ? card.attack.ToString() : "";
        canvas.Find("HP").GetComponent<TMP_Text>().text = unit ? card.health.ToString() : "";
        canvas.Find("MN").GetComponent<TMP_Text>().text = card.CostAsRoman;
        descriptionFade.GetComponentInChildren<TMP_Text>(true).text = card.description;

        // Use the card's front-facing basis before registering the marker centers.
        detailView.transform.localRotation = MarkerToCard * detailPrefab.transform.localRotation;
        detailView.transform.position += anchor.position - marker.rectTransform.position - detailView.transform.forward * 0.002f;
        UpdateReveal();
        detailView.SetActive(true);
        displayedAnchor = anchor;
        displayedCardId = cardId;
    }

    private void UpdateReveal()
    {
        float elapsed = Time.unscaledTime - phaseStart;
        float fraction = IsDetailMode ? 1f : Mathf.Min(0.99f,
            (consecutiveFrames - 2 + Mathf.Clamp01(elapsed / phaseDuration)) * 0.5f);
        revealMask.padding = new Vector4(0f, 0f, 0f, revealMask.rectTransform.rect.height * (1f - fraction));
        descriptionFade.alpha = IsDetailMode ? Mathf.Clamp01(elapsed / DescriptionFadeTime) : 0f;
    }

    private void OnDisable()
    {
        UpdateCandidate(-1);
        ShowDetailOn(null);
    }

    private void OnDestroy()
    {
        if (detailView != null) Destroy(detailView);
    }
}

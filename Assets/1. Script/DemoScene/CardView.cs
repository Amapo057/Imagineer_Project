using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Card.prefab 하나(손패/필드에 실제로 보이는 카드 비주얼)에 붙어서, 그 카드가 지금 어떤 CardData를
/// 나타내는지 화면에 보여주는 역할. 예전엔 카드를 뽑아도 이름/코스트/공격력/체력/능력이 전부
/// 프리팹에 박아둔 플레이스홀더 값 그대로였음(어떤 CardData인지 비주얼 쪽에서 아예 몰랐음) —
/// 이제 DrawManager가 카드를 뽑을 때 실제로 뽑힌 CardData를 SetCardData로 넘겨줘서 채움.
///
/// 손패에 있을 때 채워진 값은 그 카드가 나중에 필드에 놓일 때도(CardMove가 같은 GameObject를
/// 그대로 옮기므로) 그대로 유지됨 — 필드에서 다시 채워줄 필요 없음.
/// </summary>
public class CardView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI attackText;
    [SerializeField] private TextMeshProUGUI healthText;
    [SerializeField] private TextMeshProUGUI abilityText;

    [Header("자세히 보기 (선택 — 비워두면 아무 효과 없음)")]
    [Tooltip("'자세히 보기' 상태일 때 켜질 오버레이 패널(카드 일러스트+능력 설명을 크게 보여주는 자리). " +
             "아직 이 UI 자체가 안 만들어져 있어서 지금은 프리팹에 안 꽂아둬도 됨 — 비어있으면 " +
             "SetDetailMode가 조용히 아무 일도 안 함(hologramPrefab과 동일한 안전한 기본값)")]
    [SerializeField] private GameObject detailPanel;

    [Tooltip("자세히 보기 패널 안에서 카드 일러스트를 보여줄 Image. CardData.illustration이 아직 " +
             "전부 비어있어서(일러스트 미정) 지금은 꽂아둬도 빈 스프라이트만 나옴")]
    [SerializeField] private Image illustrationImage;

    public CardData Data { get; private set; }
    public bool IsDetailMode { get; private set; }

    public void SetCardData(CardData data)
    {
        Data = data;
        if (data == null) return;

        if (nameText != null) nameText.text = data.cardName;

        // 코스트는 기존 관례대로 로마 숫자로 표시(CardData.CostAsRoman 참고)
        if (costText != null) costText.text = data.CostAsRoman;

        // 마법 카드는 공격력/체력을 안 씀(둘 다 0으로 취급) — 숫자 0 대신 "-"로 표시해서
        // "이 카드는 공격/체력이 없는 카드"라는 게 한눈에 보이도록 함
        bool isSpell = data.cardType == CardType.Spell;
        if (attackText != null) attackText.text = isSpell ? "-" : data.attack.ToString();
        if (healthText != null) healthText.text = isSpell ? "-" : data.health.ToString();

        if (abilityText != null)
        {
            string keywordLabel = data.KeywordLabel;
            if (string.IsNullOrEmpty(keywordLabel))
            {
                abilityText.text = ""; // 키워드 없는 바닐라 카드는 능력 칸을 비워둠
            }
            else
            {
                abilityText.text = string.IsNullOrEmpty(data.description)
                    ? keywordLabel
                    : $"[{keywordLabel}] {data.description}";
            }
        }
    }

    // "자세히 보기" 상태 진입/이탈. 기술 문서상 실제 MR에서는 "카드를 들고 가까이 봄"으로 들어가는
    // 상태(일러스트+능력 설명을 카드 위에 크게 띄움)인데, 그 판정(손 인식 + 카메라 거리)은 아직
    // 준비 안 된 별도 시스템이라 여기서는 트리거를 안 가림 — 누가 호출하는지는 CardDetailViewer 참고
    // (PC 데모는 마우스 호버, VR은 나중에 "카드를 들고 있는지" 판정으로 교체될 자리 —
    // IFieldTargetPicker와 동일한 패턴으로, 이 클래스와 트리거 쪽을 분리해둠)
    //
    // detailPanel/illustrationImage 둘 다 아직 UI 자체가 없어서 비워둔 상태로 써도 안전하게 아무 일도
    // 안 함 — hologramPrefab이 비어있는 카드에서 SpawnHologram이 조용히 무시되는 것과 같은 패턴
    public void SetDetailMode(bool active)
    {
        IsDetailMode = active;

        if (detailPanel != null) detailPanel.SetActive(active);

        if (active && illustrationImage != null && Data != null)
        {
            illustrationImage.sprite = Data.illustration; // 일러스트 미배정 카드는 그냥 빈 스프라이트로 남음
        }
    }
}

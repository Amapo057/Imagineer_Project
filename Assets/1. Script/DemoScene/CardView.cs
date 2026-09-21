using TMPro;
using UnityEngine;

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

    public CardData Data { get; private set; }

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
}

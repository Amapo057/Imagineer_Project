using UnityEngine;

// 카드의 특수 능력(키워드). 카드 하나가 여러 개를 동시에 가질 수 있어서 Flags로 선언.
// Unity 인스펙터에서 여러 개를 동시에 체크할 수 있게 됨.
[System.Flags]
public enum CardKeyword
{
    None         = 0,
    Counter      = 1 << 0, // 반격
    Heal         = 1 << 1, // 회복
    Battlecry    = 1 << 2, // 전투의 함성
    Draw         = 1 << 3, // 드로우
    SelfDestruct = 1 << 4, // 자폭
    Cleave       = 1 << 5, // 휘둘기
}

/// <summary>
/// 카드 한 장의 데이터. 실제 게임에 등장하는 카드마다 이 타입으로
/// 에셋(.asset) 하나씩을 만들어서 사용함 (예: "화염정령.asset", "성기사.asset").
/// Unity 메뉴: Assets > Create > CardGame > Card Data
/// </summary>
[CreateAssetMenu(fileName = "NewCard", menuName = "CardGame/Card Data")]
public class CardData : ScriptableObject
{
    [Header("카드 번호")]
    [Tooltip("이 카드의 고유 번호 (1~40). 실물 카드 뒷면에 붙는 ArUco 마커 ID와 같은 값을 사용함 " +
             "— 예: 17번 카드는 마커 ID도 17. VR 쪽에서 마커를 인식하면 이 번호로 바로 카드를 찾음")]
    [Range(1, 40)]
    public int cardId;

    [Header("기본 정보")]
    public string cardName;

    [Tooltip("소속 클래스. 중립 카드는 비워두면 됨")]
    public CardClass cardClass;

    [TextArea]
    public string description;

    [Header("전투 수치")]
    [Tooltip("코스트. 표시할 땐 로마 숫자로 변환됨 (아래 CostAsRoman 참고)")]
    public int cost;
    public int attack;
    public int health;

    [Header("능력")]
    [Tooltip("이 카드가 가진 키워드. 인스펙터에서 여러 개 동시 체크 가능")]
    public CardKeyword keywords;

    [Header("실물 카드 연동")]
    [Tooltip("VR에서 이 카드 위치에 띄울 홀로그램 프리팹")]
    public GameObject hologramPrefab;

    // 코스트를 로마 숫자 문자열로 변환해서 반환 (UI 표시용).
    // 예: cost = 3 이면 "III" 반환.
    public string CostAsRoman => ToRoman(cost);

    // 이 카드가 특정 키워드를 가지고 있는지 확인.
    // 사용 예: if (card.HasKeyword(CardKeyword.Counter)) { ... }
    public bool HasKeyword(CardKeyword keyword) => (keywords & keyword) != 0;

    private static string ToRoman(int number)
    {
        if (number <= 0) return "0";

        int[] values = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] symbols = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < values.Length && number > 0; i++)
        {
            while (number >= values[i])
            {
                number -= values[i];
                result.Append(symbols[i]);
            }
        }
        return result.ToString();
    }
}

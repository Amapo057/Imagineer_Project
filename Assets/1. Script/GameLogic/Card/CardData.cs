using System.Collections.Generic;
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
    Weapon       = 1 << 6, // 무기 — 등장 시(전투의함성) 플레이어가 지정한 적 하수인에게 공격력만큼 피해
    FrontDamage  = 1 << 7, // 앞의 적 피해 — 등장 시(전투의함성) 정면 라인의 적 하수인에게 공격력만큼 피해

    // 아래 둘은 마법 카드 전용 키워드. Draw는 유닛 쪽과 완전히 같은 뜻(카드 1장 뽑기)이라 플래그를
    // 재사용하지만, Heal은 마법에서는 뜻이 다름 — "왼쪽 아군 회복"이 아니라 "명치가 맞은 횟수를 1
    // 줄임"으로 처리됨(DemoTurnController.ResolveSpellEffect 참고, 카드타입으로 이미 분기되므로 안전함)
    Remove       = 1 << 8, // 제거 — 마법 전용. 지정한 적 하수인에게 고정 피해(GameRules.RemoveSpellDamage)
    Buff         = 1 << 9, // 강화 — 마법 전용. 이번 턴이 끝날 때까지 아군 하수인 전체 공격력 증가(GameRules.BuffSpellAttackBonus)
}

// 카드 종류. 유닛은 4라인에 배치되고, 마법은 라인과 별도인 마법 전용 필드 슬롯(파란색, FieldSlot.IsSpellSlot)
// 위에서 시전 즉시 소모됨 — 유닛과 달리 PlayerBoardState에 카드 상태로 남지 않음(DemoTurnController 참고)
public enum CardType
{
    Unit,
    Spell,
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
    [Tooltip("이 카드의 고유 번호 (1~27). 실물 카드 뒷면에 붙는 ArUco 마커 ID와 같은 값을 사용함 " +
             "— 예: 17번 카드는 마커 ID도 17. VR 쪽에서 마커를 인식하면 이 번호로 바로 카드를 찾음. " +
             "예전엔 카드 종류가 40개였는데(공용/진영 20/20 미러링), 덱 구성을 공용9+진영7+마법4=27종으로 " +
             "바꾸면서 카드 종류 자체가 27개로 줄었음(cards.csv, CardDatabase 기준) — 그래서 상한도 27로 맞춤")]
    [Range(1, 27)]
    public int cardId;

    [Header("기본 정보")]
    public string cardName;

    [Tooltip("유닛인지 마법인지. 마법은 attack/health를 쓰지 않고, 필드에 남지 않은 채 즉시 효과만 발동함")]
    public CardType cardType = CardType.Unit;

    [Tooltip("소속 클래스. 중립 카드는 비워두면 됨")]
    public CardClass cardClass;

    [TextArea]
    public string description;

    [Header("전투 수치")]
    [Tooltip("코스트. 표시할 땐 로마 숫자로 변환됨 (아래 CostAsRoman 참고)")]
    public int cost;

    [Tooltip("마법 카드는 사용 안 함 (0으로 둠)")]
    public int attack;
    [Tooltip("마법 카드는 사용 안 함 (0으로 둠)")]
    public int health;

    [Header("능력")]
    [Tooltip("이 카드가 가진 키워드. 인스펙터에서 여러 개 동시 체크 가능")]
    public CardKeyword keywords;

    [Header("실물 카드 연동")]
    [Tooltip("VR에서 이 카드 위치에 띄울 홀로그램 프리팹")]
    public GameObject hologramPrefab;

    [Tooltip("'자세히 보기' 상태에서 카드 위에 크게 띄울 일러스트. 아직 카드 일러스트 자체가 " +
             "미정이라(디자인 확정 전) 지금은 27장 전부 비어있음 — hologramPrefab과 동일하게, " +
             "비어있으면 CardView.SetDetailMode가 조용히 무시하고 아무 일도 안 함")]
    public Sprite illustration;

    // 코스트를 로마 숫자 문자열로 변환해서 반환 (UI 표시용).
    // 예: cost = 3 이면 "III" 반환.
    public string CostAsRoman => ToRoman(cost);

    // 이 카드가 특정 키워드를 가지고 있는지 확인.
    // 사용 예: if (card.HasKeyword(CardKeyword.Counter)) { ... }
    public bool HasKeyword(CardKeyword keyword) => (keywords & keyword) != 0;

    // 카드가 가진 키워드를 한글 이름으로 보여주는 문자열(카드 비주얼 표시용, CardView 참고).
    // 여러 개 있으면 쉼표로 구분, 키워드가 하나도 없는 "바닐라" 카드면 빈 문자열을 돌려줌
    public string KeywordLabel
    {
        get
        {
            var names = new List<string>();
            if (HasKeyword(CardKeyword.Draw)) names.Add("드로우");
            if (HasKeyword(CardKeyword.Counter)) names.Add("반격");
            if (HasKeyword(CardKeyword.Heal)) names.Add("회복");
            if (HasKeyword(CardKeyword.Weapon)) names.Add("무기");
            if (HasKeyword(CardKeyword.FrontDamage)) names.Add("앞의 적 피해");
            if (HasKeyword(CardKeyword.Remove)) names.Add("제거");
            if (HasKeyword(CardKeyword.Buff)) names.Add("강화");
            if (HasKeyword(CardKeyword.Battlecry)) names.Add("전투의 함성");
            if (HasKeyword(CardKeyword.SelfDestruct)) names.Add("자폭");
            if (HasKeyword(CardKeyword.Cleave)) names.Add("휘둘기");
            return string.Join(", ", names);
        }
    }

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

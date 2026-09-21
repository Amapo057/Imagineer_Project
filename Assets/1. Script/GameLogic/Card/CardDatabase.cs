using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 카드 27종(공용 9 + 인간 7 + 악마 7 + 마법 4) 전체를 한 곳에 모아두는 데이터베이스 에셋.
/// 데모든 나중에 만들 실제 게임이든 "카드 풀 전체"가 필요한 곳(덱 빌드 등)에서는 이 에셋
/// 하나만 참조하면 되게 하려고 만듦 — 씬마다 CardData를 27개씩 손으로 끌어다 놓을 필요가 없어짐.
///
/// GameLogic 폴더 소속(=씬/컴포넌트에 의존하지 않는 순수 로직)이라, 나중에 실제 게임 씬을 만들 때도
/// 이 에셋 + BuildShuffledDeck()을 그대로 재사용하면 됨(디자인만 이 안에 있고, "언제/어떻게 호출할지"는
/// 데모든 실제 게임이든 각자의 컨트롤러가 정하면 됨 — IFieldTargetPicker와 같은 패턴).
///
/// 공용/인간/악마/마법 구분은 cardId 범위로 판단함(기획 문서 cards.csv 기준: 1~9=공용, 10~16=인간,
/// 17~23=악마, 24~27=마법). CardData 자체에는 진영을 나타내는 필드가 없고, CSV/CardCsvImporter도
/// 건드리지 않기 위해 일부러 여기서만 범위로 분류함 — 카드 종류나 번호 범위가 나중에 바뀌면
/// 이 상수들만 같이 고치면 됨.
///
/// Unity 메뉴: Assets > Create > CardGame > Card Database
/// </summary>
[CreateAssetMenu(fileName = "CardDatabase", menuName = "CardGame/Card Database")]
public class CardDatabase : ScriptableObject
{
    // 플레이어가 고를 수 있는 진영. 공용/마법은 진영과 무관하게 모두가 공유해서 쓰므로 여기 없음
    public enum Faction
    {
        Human, // 인간 (cardId 10~16)
        Demon, // 악마 (cardId 17~23)
    }

    private const int CommonMinId = 1, CommonMaxId = 9;
    private const int HumanMinId = 10, HumanMaxId = 16;
    private const int DemonMinId = 17, DemonMaxId = 23;
    private const int SpellMinId = 24, SpellMaxId = 27;

    [Tooltip("27종 카드 전체(공용9 + 인간7 + 악마7 + 마법4). Assets/2. Data/Card 아래 .asset 27개를 " +
             "전부 끌어다 놓으면 됨. 새 카드가 추가되면 여기에도 같이 추가해야 함")]
    public List<CardData> allCards = new List<CardData>();

    public IEnumerable<CardData> CommonCards => InRange(CommonMinId, CommonMaxId);
    public IEnumerable<CardData> SpellCards => InRange(SpellMinId, SpellMaxId);

    public IEnumerable<CardData> FactionCards(Faction faction)
    {
        return faction == Faction.Human
            ? InRange(HumanMinId, HumanMaxId)
            : InRange(DemonMinId, DemonMaxId);
    }

    private IEnumerable<CardData> InRange(int minId, int maxId)
    {
        return allCards.Where(c => c != null && c.cardId >= minId && c.cardId <= maxId);
    }

    // 숫자 하나(cardId)로 카드 하나를 바로 찾음. 마커 인식 결과처럼 "번호 하나만 아는 상태"에서
    // 그 번호에 해당하는 카드 정보(이름/수치/능력/홀로그램)를 바로 가져오고 싶을 때 씀.
    // 없는 번호면 null을 돌려줌(호출부에서 null 체크 필요)
    public CardData GetCardById(int cardId)
    {
        return allCards.FirstOrDefault(c => c != null && c.cardId == cardId);
    }

    // 실제 플레이어 덱 구성 그대로: 공용 9 + 선택한 진영 7 + 마법 4 = 20장을 만들고,
    // 카드 순서를 랜덤 셔플해서 반환함. "덱에서 카드를 뽑는" 로직(GameManager.TryDrawCard)은
    // 항상 deck[0]을 가져가는 방식이라, 실제 무작위성은 여기서 순서를 섞는 걸로 전부 처리됨.
    //
    // rng를 안 넘기면 새 System.Random을 매번 만들어 씀 — 재현 가능한 테스트가 필요하면
    // 호출하는 쪽에서 시드를 고정한 System.Random을 넘기면 됨
    public List<CardData> BuildShuffledDeck(Faction faction, System.Random rng = null)
    {
        var deck = new List<CardData>();
        deck.AddRange(CommonCards);
        deck.AddRange(FactionCards(faction));
        deck.AddRange(SpellCards);

        Shuffle(deck, rng ?? new System.Random());
        return deck;
    }

    // Fisher-Yates 셔플
    private static void Shuffle<T>(IList<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

/// <summary>
/// 게임 밸런스 관련 숫자들을 한곳에 모아둔 설정값.
/// 나중에 수치를 바꾸고 싶으면 로직 코드를 뒤질 필요 없이 여기 값만 고치면 됨.
/// </summary>
public static class GameRules
{
    // 필드 라인 개수
    public const int LaneCount = 4;

    // 플레이어 한 명당 덱에 들어가는 카드 수
    public const int DeckSize = 20;

    public const int StartingHeroHealth = 20;
    // Kept for existing scene/debug scripts. This is now a damage threshold.
    public const int FaceHitThreshold = StartingHeroHealth;

    // 코스트는 자기 턴 하나 지날 때마다 1씩 늘어나고, 최대치까지만 차오름
    public const int MaxCost = 8;

    // 후공은 첫 턴부터 이 코스트로 시작하고, 두 번째 턴까지는 그대로 유지됨
    // (세 번째 턴부터는 선공과 같은 속도로 1씩 증가)
    public const int SecondPlayerStartingCost = 2;

    // 손패 최대 장수. 이미 가득 찬 상태에서 드로우 효과(카드/키워드 무관)가 발동하면 그냥 아무 일도
    // 안 일어남 — 카드가 사라지지 않고 덱에 그대로 남음(GameManager.TryDrawCard 참고)
    public const int MaxHandSize = 12;

    // 심판의낙뢰: 아군과 적군의 모든 하수인에게 주는 피해량.
    public const int RemoveSpellDamage = 4;

    // 전쟁의함성: 시전할 때 필드에 있는 아군 하수인의 공격력을 영구적으로 증가시킴.
    public const int BuffSpellAttackBonus = 3;
}

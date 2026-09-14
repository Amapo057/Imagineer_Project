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

    // 명치를 이 횟수만큼 맞으면 패배
    public const int FaceHitThreshold = 10;

    // 코스트는 매 턴 이만큼씩 늘어나고, 최대치까지만 차오름
    public const int CostPerTurn = 1;
    public const int MaxCost = 8;

    // 후공 코스트 보정
    public const int SecondPlayerBonusCost = 2;
}

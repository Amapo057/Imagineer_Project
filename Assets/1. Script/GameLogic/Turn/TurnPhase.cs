/// <summary>
/// 한 플레이어의 턴 안에서 도는 페이즈 순서.
/// TurnStart → Main → EndTurn → Combat 순으로 진행되고,
/// Combat이 끝나면 상대 턴의 TurnStart로 넘어감.
/// </summary>
public enum TurnPhase
{
    TurnStart,
    Main,
    EndTurn,
    Combat
}

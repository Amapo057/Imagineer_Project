/// <summary>
/// 게임 전체 보드 상태. 나(Me)와 상대(Opponent)의 PlayerBoardState를 같이 관리함.
/// 나중에 만들 턴 상태 머신(GameStateManager)이 페이즈가 바뀔 때 이 클래스를 들여다보고 조작하게 됨.
/// </summary>
public class GameBoardState
{
    public PlayerBoardState me = new PlayerBoardState { side = PlayerSide.Me };
    public PlayerBoardState opponent = new PlayerBoardState { side = PlayerSide.Opponent };

    // side에 맞는 보드 상태를 바로 가져오는 헬퍼
    public PlayerBoardState GetBoard(PlayerSide side) => side == PlayerSide.Me ? me : opponent;

    // 상대편 보드를 가져오는 헬퍼 (공격 대상 찾을 때 자주 씀)
    public PlayerBoardState GetOpponentBoard(PlayerSide side) => side == PlayerSide.Me ? opponent : me;
}

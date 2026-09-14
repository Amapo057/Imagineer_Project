using System;

/// <summary>
/// 턴/페이즈 진행을 관리하는 상태 머신 (이벤트 기반).
/// 페이즈가 바뀔 때 이벤트만 쏘고, 실제로 무슨 일이 일어나는지는 각 시스템(능력, UI 등)이
/// 그 이벤트를 구독해서 알아서 처리함 — 이 클래스는 능력이나 UI 코드를 전혀 몰라도 됨.
///
/// 일반 C# 클래스라서 씬에 오브젝트로 안 만들어도 됨. 다른 매니저 스크립트에서
/// new GameStateManager() 로 만들어서 들고 있으면 됨.
///
/// 사용 예 (능력/UI 쪽 코드에서):
/// gameState.OnPhaseEnter += phase =>
/// {
///     if (phase == TurnPhase.Combat) ResolveAllAttacks();
/// };
/// </summary>
public class GameStateManager
{
    // 페이즈가 진행되는 순서
    private static readonly TurnPhase[] PhaseOrder =
    {
        TurnPhase.TurnStart,
        TurnPhase.Main,
        TurnPhase.EndTurn,
        TurnPhase.Combat
    };

    public TurnPhase CurrentPhase { get; private set; }
    public PlayerSide CurrentPlayer { get; private set; }

    // 페이즈에 들어갈 때 / 나갈 때 쏘는 이벤트. 다른 시스템은 여기에 구독하면 됨
    public event Action<TurnPhase> OnPhaseEnter;
    public event Action<TurnPhase> OnPhaseExit;

    // 턴이 다른 플레이어에게 넘어갈 때 쏘는 이벤트
    public event Action<PlayerSide> OnTurnChanged;

    // 게임 시작 — 선공 플레이어를 정하고 첫 페이즈(TurnStart)로 진입
    public void StartGame(PlayerSide firstPlayer)
    {
        CurrentPlayer = firstPlayer;
        CurrentPhase = PhaseOrder[0];
        OnPhaseEnter?.Invoke(CurrentPhase);
    }

    // 다음 페이즈로 넘어감.
    // Combat 다음은 다시 TurnStart이고, 이때 턴 주인이 상대로 넘어감
    public void AdvancePhase()
    {
        OnPhaseExit?.Invoke(CurrentPhase);

        int currentIndex = Array.IndexOf(PhaseOrder, CurrentPhase);
        int nextIndex = (currentIndex + 1) % PhaseOrder.Length;
        CurrentPhase = PhaseOrder[nextIndex];

        if (CurrentPhase == TurnPhase.TurnStart)
        {
            CurrentPlayer = CurrentPlayer == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me;
            OnTurnChanged?.Invoke(CurrentPlayer);
        }

        OnPhaseEnter?.Invoke(CurrentPhase);
    }
}

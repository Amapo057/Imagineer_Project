using System;
using UnityEngine;

/// <summary>
/// GameStateManager(턴/페이즈 진행)와 GameBoardState(보드 상태)를 실제로 이어붙이는 총괄 매니저.
/// 페이즈가 바뀔 때마다 여기서 보드 상태를 조작하고(코스트 충전, 드로우, 전투 처리), 승패를 체크함.
///
/// 아직 여기 없는 것: 손패에서 카드를 골라 라인에 내는 처리(메인 페이즈 중 실제 "카드 내기")는
/// UI/VR 입력이 있어야 하는 부분이라 나중에 따로 붙일 예정.
/// </summary>
public class GameManager
{
    public GameStateManager State { get; } = new GameStateManager();
    public GameBoardState Board { get; } = new GameBoardState();

    // 게임이 끝났을 때 쏘는 이벤트 (이긴 쪽을 알려줌)
    public event Action<PlayerSide> OnGameOver;

    private bool isGameOver;

    // 지금이 게임 전체에서 몇 번째 턴인지. 1이면 "첫 턴 공격 불가" 규칙이 적용됨
    private int turnNumber = 1;

    public GameManager()
    {
        State.OnPhaseEnter += HandlePhaseEnter;
        State.OnTurnChanged += _ => turnNumber++;
    }

    // 게임 시작. firstPlayer를 안 정해주면 50%로 랜덤하게 선공을 정함
    public void StartGame(PlayerSide? firstPlayer = null)
    {
        isGameOver = false;
        turnNumber = 1;

        PlayerSide starter = firstPlayer ?? (UnityEngine.Random.value < 0.5f ? PlayerSide.Me : PlayerSide.Opponent);
        PlayerSide second = starter == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me;

        Board.GetBoard(starter).isSecondPlayer = false;
        Board.GetBoard(starter).ownTurnCount = 0;
        Board.GetBoard(second).isSecondPlayer = true;
        Board.GetBoard(second).ownTurnCount = 0;

        State.StartGame(starter);
    }

    private void HandlePhaseEnter(TurnPhase phase)
    {
        if (isGameOver) return;

        switch (phase)
        {
            case TurnPhase.TurnStart:
                HandleTurnStart();
                break;
            case TurnPhase.Combat:
                HandleCombat();
                break;
        }
    }

    // 턴 시작: 코스트 충전하고, 카드 한 장 드로우하고, 공격 여부를 초기화함
    private void HandleTurnStart()
    {
        var board = Board.GetBoard(State.CurrentPlayer);

        board.ownTurnCount++;

        // 선공: 본인 턴 횟수 = 코스트 (1, 2, 3, ...)
        // 후공: 첫 턴부터 SecondPlayerStartingCost(2)로 시작해서 두 번째 턴까지 그대로 유지,
        //       세 번째 턴부터는 선공과 같은 속도(1씩)로 증가
        int baseCost = board.isSecondPlayer
            ? Math.Max(board.ownTurnCount, GameRules.SecondPlayerStartingCost)
            : board.ownTurnCount;

        board.maxCost = Math.Min(baseCost, GameRules.MaxCost);
        board.currentCost = board.maxCost;

        foreach (var card in board.lanes)
        {
            if (card != null) card.hasAttackedThisTurn = false;
        }
        if (board.spellSlot != null) board.spellSlot.hasAttackedThisTurn = false;

        if (board.deck.Count > 0)
        {
            var drawn = board.deck[0];
            board.deck.RemoveAt(0);
            board.hand.Add(drawn);
        }
    }

    // 전투 페이즈: 현재 턴 플레이어의 4라인이 상대의 같은 라인을 공격.
    // 상대 라인이 비어있으면 명치를 때림. 첫 턴에는 아예 공격이 일어나지 않음
    private void HandleCombat()
    {
        if (turnNumber == 1) return;

        var attackerBoard = Board.GetBoard(State.CurrentPlayer);
        var defenderBoard = Board.GetOpponentBoard(State.CurrentPlayer);

        for (int i = 0; i < GameRules.LaneCount; i++)
        {
            var attacker = attackerBoard.lanes[i];
            if (attacker == null || !attacker.IsAlive) continue;

            var defender = defenderBoard.lanes[i];
            if (defender != null && defender.IsAlive)
            {
                defender.TakeDamage(attacker.currentAttack);

                // "반격" 능력이 있을 때만 공격자도 피해를 입음 (기본은 공격자 무피해)
                if (defender.data.HasKeyword(CardKeyword.Counter))
                {
                    attacker.TakeDamage(defender.currentAttack);
                }
            }
            else
            {
                defenderBoard.TakeFaceHit();
            }

            attacker.hasAttackedThisTurn = true;
        }

        attackerBoard.RemoveDeadCards();
        defenderBoard.RemoveDeadCards();

        CheckGameOver();
    }

    private void CheckGameOver()
    {
        if (isGameOver) return;

        if (Board.me.IsDefeated)
        {
            isGameOver = true;
            OnGameOver?.Invoke(PlayerSide.Opponent);
        }
        else if (Board.opponent.IsDefeated)
        {
            isGameOver = true;
            OnGameOver?.Invoke(PlayerSide.Me);
        }
    }
}

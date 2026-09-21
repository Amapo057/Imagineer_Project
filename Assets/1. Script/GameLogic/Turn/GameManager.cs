using System;
using UnityEngine;

/// <summary>
/// GameStateManager(턴/페이즈 진행)와 GameBoardState(보드 상태)를 실제로 이어붙이는 총괄 매니저.
/// 페이즈가 바뀔 때마다 여기서 보드 상태를 조작하고(코스트 충전, 드로우, 전투 처리), 승패를 체크함.
///
/// 손패에서 카드를 골라 실제로 내는 처리(메인 페이즈 중 "카드 내기")는 UI/VR 입력이 있어야 하는
/// 부분이라 여기 없음 — DemoTurnController.TryPlayCard가 이 GameManager를 통해 처리함.
/// 카드 자체의 키워드 효과(회복/무기/앞의 적 피해/제거/강화 등)는 이 클래스의 Apply*/Reduce* 메서드로
/// 노출해두고, "언제 그 메서드를 부를지"는 DemoTurnController(ResolveBattlecry/ResolveSpellEffect)가 정함.
/// </summary>
public class GameManager
{
    public GameStateManager State { get; } = new GameStateManager();
    public GameBoardState Board { get; } = new GameBoardState();

    // 게임이 끝났을 때 쏘는 이벤트. 이긴 쪽을 알려주고, 무승부면 null이 넘어옴
    // (무승부는 "둘 다 필드+손+덱에 카드가 한 장도 없을 때 명치 맞은 횟수가 같은 경우"에만 발생)
    public event Action<PlayerSide?> OnGameOver;

    private bool isGameOver;
    public bool IsGameOver => isGameOver;

    // 지금이 게임 전체에서 몇 번째 턴인지 (선공/후공 턴을 합쳐서 셈). 1이면 "첫 턴 공격 불가" 규칙이 적용됨
    private int turnNumber = 1;
    public int TurnNumber => turnNumber;

    public GameManager()
    {
        State.OnPhaseEnter += HandlePhaseEnter;
        State.OnTurnChanged += _ => turnNumber++;
    }

    // 게임 시작. firstPlayer를 안 정해주면 50%로 랜덤하게 선공을 정함.
    // 시작하자마자 TurnStart 처리(코스트/드로우)까지 끝내고 Main 페이즈에서 대기함
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

        // TurnStart는 준비 단계일 뿐이라, 플레이어가 실제로 행동할 수 있는 Main 페이즈까지 바로 진행시킴
        AdvanceToMain();
    }

    // 턴 종료 버튼에서 호출. 남은 페이즈(EndTurn, Combat)를 전부 진행시켜서
    // 상대 턴의 Main 페이즈까지 한 번에 넘어감 (이 데모에서만 쓰는 간단한 흐름)
    public void EndTurn()
    {
        if (isGameOver) return;
        AdvanceToMain();
    }

    // 게임오버가 아니면, Main 페이즈에 도달할 때까지 페이즈를 계속 진행시킴.
    // 반드시 최소 한 번은 AdvancePhase()를 호출해야 함 — EndTurn()에서 호출될 때는
    // 이미 Main 페이즈인 상태로 들어오기 때문에, while로 조건부터 검사하면 아무 일도 안 일어남
    // (버튼을 눌러도 턴이 전혀 안 넘어가던 버그의 원인이었음)
    private void AdvanceToMain()
    {
        do
        {
            State.AdvancePhase();
        }
        while (!isGameOver && State.CurrentPhase != TurnPhase.Main);
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

        // 드로우는 더 이상 여기서 자동으로 안 함 — TryDrawCard(side)를 통해 Draw 버튼을
        // 눌렀을 때만 일어나도록 데모 쪽(DemoTurnController)에서 명시적으로 호출함
    }

    // 특정 편의 덱에서 카드 한 장을 손패로 가져옴. Draw 버튼 클릭 + 드로우 키워드(유닛/마법 공통)에서 호출됨.
    // 덱이 비어있거나 게임이 끝났으면 아무 일도 안 하고 false를 돌려줌.
    // 손패가 이미 GameRules.MaxHandSize만큼 가득 찼으면도 마찬가지로 false — 이때 카드는
    // 사라지지 않고 그냥 덱에 남음(디자인 문서의 "손패" 규칙 그대로)
    public bool TryDrawCard(PlayerSide side)
    {
        if (isGameOver) return false;

        var board = Board.GetBoard(side);
        if (board.deck.Count == 0) return false;
        if (board.hand.Count >= GameRules.MaxHandSize) return false;

        var drawn = board.deck[0];
        board.deck.RemoveAt(0);
        board.hand.Add(drawn);
        return true;
    }

    // 회복 키워드: side편 laneIndex 라인에 막 나온 카드가, 자기 "왼쪽"(laneIndex-1) 아군의 체력을
    // amount만큼 회복시킴. 왼쪽에 라인이 없거나(맨 왼쪽) 왼쪽이 비어있으면 아무 일도 안 일어남.
    // 원본 카드 체력(data.health)을 넘는 초과 회복은 안 되도록 막음
    public void ApplyHealToLeftAlly(PlayerSide side, int laneIndex, int amount)
    {
        if (isGameOver) return;

        var board = Board.GetBoard(side);
        int leftIndex = laneIndex - 1;
        if (leftIndex < 0) return;

        var ally = board.lanes[leftIndex];
        if (ally == null) return;

        ally.currentHealth = Math.Min(ally.currentHealth + amount, ally.data.health);
    }

    // 앞의 적 피해 키워드: side편 laneIndex 라인에 막 나온 카드가, 상대의 "같은 라인"에 있는
    // 적 하수인에게 amount만큼 피해를 줌. 정면에 적이 없으면 아무 일도 안 일어남(명치 피해 없음)
    public void ApplyFrontDamage(PlayerSide side, int laneIndex, int amount)
    {
        if (isGameOver) return;

        var defenderBoard = Board.GetOpponentBoard(side);
        var target = defenderBoard.lanes[laneIndex];
        if (target == null) return;

        target.TakeDamage(amount);
        defenderBoard.RemoveDeadCards();
        CheckGameOver();
    }

    // 무기 키워드 전용: 플레이어(또는 VR에서 손으로 지목)가 직접 고른 대상(side편 laneIndex 라인)에게
    // amount만큼 피해를 줌. ApplyFrontDamage와 달리 대상이 "누가 골랐든 이미 확정된 좌표"라서
    // side를 상대 진영 그대로 받음 (호출부에서 이미 적 진영의 슬롯을 넘겨줌)
    public void ApplyDamageToLane(PlayerSide side, int laneIndex, int amount)
    {
        if (isGameOver) return;

        var board = Board.GetBoard(side);
        var target = board.lanes[laneIndex];
        if (target == null) return;

        target.TakeDamage(amount);
        board.RemoveDeadCards();
        CheckGameOver();
    }

    // 마법 "강화" 전용: side편 필드에 지금 나와 있는 아군 하수인 전체의 공격력을 amount만큼 즉시 올림.
    // "이번 턴이 끝날 때까지"만 유지되는 버프라 CardInstance.tempAttackBonus에 따로 적립해두고,
    // 그 편의 전투 페이즈(HandleCombat)가 끝나는 시점에 자동으로 원상복구됨
    public void ApplyTurnEndBuffToAllies(PlayerSide side, int amount)
    {
        if (isGameOver) return;

        var board = Board.GetBoard(side);
        foreach (var card in board.lanes)
        {
            if (card == null) continue;
            card.currentAttack += amount;
            card.tempAttackBonus += amount;
        }
    }

    // 마법 "회복"(명치) 전용: side편이 지금까지 명치를 맞은 횟수를 amount만큼 줄임(0 밑으로는 안 내려감).
    // 유닛의 회복 키워드(ApplyHealToLeftAlly, 왼쪽 아군 체력 회복)와는 이름만 같고 완전히 다른 효과임
    public void ReduceFaceHitCount(PlayerSide side, int amount)
    {
        if (isGameOver) return;

        var board = Board.GetBoard(side);
        board.faceHitCount = Math.Max(0, board.faceHitCount - amount);
    }

    // 전투 페이즈: 현재 턴 플레이어의 4라인이 상대의 같은 라인을 공격.
    // 상대 라인이 비어있으면 명치를 때림. 첫 턴에는 아예 공격이 일어나지 않음(그래도 강화 버프
    // 정리/사망 카드 정리는 그대로 해야 해서 turnNumber==1이어도 메서드 끝까지는 진행함)
    private void HandleCombat()
    {
        var attackerBoard = Board.GetBoard(State.CurrentPlayer);
        var defenderBoard = Board.GetOpponentBoard(State.CurrentPlayer);

        if (turnNumber != 1)
        {
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
        }

        // "강화" 마법으로 이번 턴에 붙은 임시 공격력은 이 턴의 전투 페이즈가 지나가면 사라짐
        // (1턴째처럼 공격이 아예 없었어도 버프는 그대로 정리되어야 함)
        foreach (var card in attackerBoard.lanes)
        {
            if (card != null && card.tempAttackBonus != 0)
            {
                card.currentAttack -= card.tempAttackBonus;
                card.tempAttackBonus = 0;
            }
        }

        attackerBoard.RemoveDeadCards();
        defenderBoard.RemoveDeadCards();

        CheckGameOver();
    }

    // 승패 체크. 우선순위:
    // 1) 명치를 FaceHitThreshold번 맞은 쪽이 있으면 그 반대쪽이 승리
    // 2) 둘 다 필드+손+덱에 카드가 한 장도 없으면, 명치를 덜 맞은(체력이 더 많이 남은) 쪽이 승리
    //    (똑같이 맞았으면 무승부)
    // public인 이유: 아직 "카드로 직접 공격" 로직이 없어서, 데모에서 명치 피해를 임의로 넣어보고
    // (DemoTurnController의 디버그 버튼 등) 바로 승패 체크를 다시 돌려보기 위함
    public void CheckGameOver()
    {
        if (isGameOver) return;

        if (Board.me.IsDefeated)
        {
            EndGame(PlayerSide.Opponent);
            return;
        }
        if (Board.opponent.IsDefeated)
        {
            EndGame(PlayerSide.Me);
            return;
        }

        if (Board.me.HasNoCards && Board.opponent.HasNoCards)
        {
            if (Board.me.faceHitCount < Board.opponent.faceHitCount)
            {
                EndGame(PlayerSide.Me);
            }
            else if (Board.opponent.faceHitCount < Board.me.faceHitCount)
            {
                EndGame(PlayerSide.Opponent);
            }
            else
            {
                EndGame(null); // 무승부
            }
        }
    }

    private void EndGame(PlayerSide? winner)
    {
        isGameOver = true;
        OnGameOver?.Invoke(winner);
    }
}

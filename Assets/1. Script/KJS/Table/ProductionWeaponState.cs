using System.Collections.Generic;
using System.Linq;

// Match-local, one-use battlecry charges; damage is derived locally, never trusted from the wire.
public sealed class ProductionWeaponState
{
    struct Charge { public int id, damage; }
    readonly Queue<Charge>[] charges = { new Queue<Charge>(), new Queue<Charge>() };
    readonly HashSet<int>[] armed = { new HashSet<int>(), new HashSet<int>() };
    GameManager game;
    int turn = -1;
    void Sync(GameManager current)
    {
        if (game != current) { game = current; foreach (var set in armed) set.Clear(); turn = -1; }
        if (turn == game.TurnNumber && !game.IsGameOver) return;
        turn = game.TurnNumber; foreach (var queue in charges) queue.Clear();
    }
    public void Arm(GameManager current, PlayerSide side, CardData card)
    {
        Sync(current);
        if (game.IsGameOver || game.State.CurrentPlayer != side || !card.HasKeyword(CardKeyword.Weapon) || !armed[(int)side].Add(card.cardId)) return;
        if (!game.Board.GetOpponentBoard(side).lanes.Any(c => c != null && c.IsAlive)) return;
        charges[(int)side].Enqueue(new Charge { id = card.cardId, damage = card.attack });
    }
    public int Pending(GameManager current, PlayerSide side) { Sync(current); return charges[(int)side].Count; }
    public int Source(GameManager current, PlayerSide side) => Pending(current, side) == 0 ? -1 : charges[(int)side].Peek().id;
    public bool CanStrike(GameManager current, PlayerSide side, int source, int lane, int expectedTurn)
    {
        Sync(current);
        return !game.IsGameOver && game.State.CurrentPlayer == side && game.TurnNumber == expectedTurn &&
            Source(current, side) == source && source > 0 && lane >= 0 && lane < GameRules.LaneCount &&
            game.Board.GetOpponentBoard(side).lanes[lane] != null && game.Board.GetOpponentBoard(side).lanes[lane].IsAlive;
    }
    public bool Strike(GameManager current, PlayerSide side, int source, int lane, int expectedTurn)
    {
        if (!CanStrike(current, side, source, lane, expectedTurn)) return false;
        var charge = charges[(int)side].Dequeue();
        game.EquipAndSwingWeapon(side, charge.damage, side == PlayerSide.Me ? PlayerSide.Opponent : PlayerSide.Me, lane);
        return true;
    }
}

namespace KJH.Balancing
{
    // One token outside the 20-card deck. It grants only spendable mana this turn.
    public sealed class SecondPlayerCoin
    {
        readonly GameManager game;
        readonly bool[] used = new bool[2];
        readonly int[] usedOnOwnTurn = new int[2];

        public SecondPlayerCoin(GameManager game) { this.game = game; }

        public bool IsAvailable(PlayerSide side) => !used[(int)side] && game.Board.GetBoard(side).isSecondPlayer;

        public bool CanUse(PlayerSide side) => !game.IsGameOver && game.State.CurrentPlayer == side &&
            IsAvailable(side) && game.Board.GetBoard(side).currentCost < GameRules.MaxCost;

        public bool TryUse(PlayerSide side)
        {
            if (!CanUse(side)) return false;
            used[(int)side] = true;
            usedOnOwnTurn[(int)side] = game.Board.GetBoard(side).ownTurnCount;
            game.Board.GetBoard(side).currentCost++;
            return true;
        }

        public int DisplayCapacity(PlayerSide side)
        {
            var board = game.Board.GetBoard(side);
            bool bonus = used[(int)side] && usedOnOwnTurn[(int)side] == board.ownTurnCount;
            return System.Math.Min(GameRules.MaxCost, board.maxCost + (bonus ? 1 : 0));
        }
    }
}

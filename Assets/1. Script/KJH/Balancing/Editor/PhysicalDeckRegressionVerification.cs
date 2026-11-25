using System;
using UnityEngine;

namespace KJH.Balancing
{
    public static class PhysicalDeckRegressionVerification
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

        static void ConfirmAll(PlayerSide side)
        {
            while (VRGameSession.ConfirmPhysicalDraw(side))
                VRGameSession.SetPhysicalDeckRemaining(side,
                    GameRules.DeckSize - VRGameSession.ConfirmedPhysicalDraws(side));
            while (VRGameSession.ConsumePhysicalCard(side)) { }
        }

        public static void Run()
        {
            PhysicalDrawVerification.Run();
            var side = PlayerSide.Me;
            VRGameSession.ResetGame(trackDeck: true);
            ConfirmAll(side);
            var game = VRGameSession.Game;
            game.DrawCards(side, 6); ConfirmAll(side);
            game.DrawCards(side, 6); ConfirmAll(side);
            Check(VRGameSession.RemainingPhysicalDeck(side) == 1, "Expected one real card left");
            game.DrawCards(side, 3);
            Check(VRGameSession.PendingPhysicalDraws(side) == 1 && game.Board.me.fatigueDamage == 0,
                "Mixed draw must reserve one card and defer two fatigue hits");
            // A deck-state update must not erase the last card's pending confirmation.
            VRGameSession.SetPhysicalDeckRemaining(side, 0);
            Check(VRGameSession.PendingPhysicalDraws(side) == 1 && game.Board.me.HeroHealth == 25,
                "Last real card confirmation was lost or fatigue applied too early");
            Check(VRGameSession.ConfirmPhysicalDraw(side), "Last real card could not be confirmed");
            Check(game.Board.me.fatigueDamage == 2 && game.Board.me.HeroHealth == 22,
                "Mixed draw must apply fatigue 1+2 after confirming the last card");
            game.DrawCards(side, 1);
            Check(game.Board.me.fatigueDamage == 3 && game.Board.me.HeroHealth == 19,
                "Following empty draw must apply fatigue 3 exactly once");
            VRGameSession.SetPhysicalDeckRemaining(side, 0);
            Check(game.Board.me.HeroHealth == 19, "Repeated deck update reapplied fatigue");

            game.DrawCards(PlayerSide.Opponent, 30);
            VRGameSession.ApplyPhysicalDeckState(0, 2);
            VRGameSession.ApplyPhysicalDeckState(0, 2);
            Check(game.Board.opponent.fatigueDamage == 2 && game.Board.opponent.HeroHealth == 22,
                "Peer must mirror the owner's fatigue exactly once");
            VRGameSession.ResetGame(trackDeck: true);
            Check(VRGameSession.PendingPhysicalDraws(side) == 7 && VRGameSession.RemainingPhysicalDeck(side) == 20 &&
                VRGameSession.Game.Board.me.fatigueDamage == 0, "Rematch retained deck/fatigue state");
            VerifyDeferredCardRecognition();
            Check(System.IO.File.Exists(ProductionGameBuilder.StartScenePath) && System.IO.File.Exists(ProductionGameBuilder.ScenePath),
                "APK scene paths must point to the current start and main scenes");
            Debug.Log("PHYSICAL_DECK_REGRESSION_PASS: legacy draws, tracked mixed/empty draws, deferred confirmation, peer sync, reset");
        }

        static void VerifyDeferredCardRecognition()
        {
            var multiplayer = MultiplayerManager.GetOrCreate(null);
            var go = new GameObject("Deferred draw verification");
            var deck = go.AddComponent<DeckManager>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var record = typeof(DeckManager).GetMethod("RecordDrawnCard", flags);
            var retry = typeof(DeckManager).GetMethod("ConfirmPendingCards", flags);
            bool required = VRGameSession.RequireNetwork, ready = VRGameSession.NetworkReady;
            try
            {
                int registered = deck.RegisteredCount; // Bind to this match before observing cards.
                VRGameSession.RequireNetwork = true;
                VRGameSession.NetworkReady = false;
                record.Invoke(deck, new object[] { 1, true });
                record.Invoke(deck, new object[] { 1, true });
                record.Invoke(deck, new object[] { 2, true });
                Check(VRGameSession.PhysicalHandCount(PlayerSide.Me) == 0 && deck.RemainingCount == 20,
                    "Blocked recognition was prematurely committed");
                VRGameSession.RequireNetwork = false;
                retry.Invoke(deck, null); // No new marker frame is needed after the block ends.
                retry.Invoke(deck, null);
                Check(VRGameSession.PhysicalHandCount(PlayerSide.Me) == 2 && deck.RemainingCount == 18,
                    "Deferred recognition was lost or counted twice");
                VRGameSession.RequireNetwork = true;
                record.Invoke(deck, new object[] { 3, true });
                VRGameSession.ResetGame(trackDeck: true);
                registered = deck.RegisteredCount;
                VRGameSession.RequireNetwork = false;
                retry.Invoke(deck, null);
                Check(registered == 0 && VRGameSession.PhysicalHandCount(PlayerSide.Me) == 0 && deck.RemainingCount == 20,
                    "Rematch retained a queued card from the previous game");
            }
            finally
            {
                VRGameSession.RequireNetwork = required;
                VRGameSession.NetworkReady = ready;
                UnityEngine.Object.DestroyImmediate(go);
                UnityEngine.Object.DestroyImmediate(multiplayer.gameObject);
            }
        }
    }
}

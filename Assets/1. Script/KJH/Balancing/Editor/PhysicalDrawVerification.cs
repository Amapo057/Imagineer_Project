using System;
using UnityEngine;

namespace KJH.Balancing
{
    public static class PhysicalDrawVerification
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        static void ConfirmAll(PlayerSide side) { while (VRGameSession.ConfirmPhysicalDraw(side)) { } }
        static void EmptyHand(PlayerSide side) { while (VRGameSession.ConsumePhysicalCard(side)) { } }

        public static void Run()
        {
            var side = PlayerSide.Me;
            VRGameSession.ResetGame();
            Check(VRGameSession.PendingPhysicalDraws(side) == 7 && VRGameSession.ConfirmedPhysicalDraws(side) == 0 &&
                VRGameSession.RemainingPhysicalDeck(side) == 20 && VRGameSession.PhysicalHandCount(side) == 0, "Opening must await physical confirmation");
            Check(!VRGameSession.ConsumePhysicalCard(side), "Empty hand consumed");
            VRGameSession.ConfirmPhysicalDraw(side);
            Check(VRGameSession.RemainingPhysicalDeck(side) == 19 && VRGameSession.PhysicalHandCount(side) == 1 &&
                VRGameSession.ConfirmedPhysicalDraws(side) == 1, "Confirmed draw did not transfer deck to hand");
            ConfirmAll(side);
            Check(!VRGameSession.ConfirmPhysicalDraw(side) && VRGameSession.ConfirmedPhysicalDraws(side) == 7, "Extra confirmation created card");
            Check(VRGameSession.ConfirmedPhysicalDraws(PlayerSide.Opponent) == 0, "Sides share draw counters");
            var game = VRGameSession.Game;
            game.DrawCards(side, 30);
            Check(VRGameSession.PendingPhysicalDraws(side) == 5 && game.Board.me.fatigueDamage == 0, "Pending draws must reserve the 12-card hand cap");
            ConfirmAll(side); game.DrawCards(side, 3);
            Check(VRGameSession.PhysicalHandCount(side) == 12 && VRGameSession.PendingPhysicalDraws(side) == 0 &&
                VRGameSession.RemainingPhysicalDeck(side) == 8, "Full hand consumed deck or granted extra draws");

            var spell = ScriptableObject.CreateInstance<CardData>();
            spell.cardId = 24; spell.cardType = CardType.Spell; spell.keywords = CardKeyword.Draw; spell.cost = 1;
            game.Board.me.currentCost = 8;
            Check(VRGameSession.TryCastPhysicalSpell(spell, side) && VRGameSession.PhysicalHandCount(side) == 11 &&
                VRGameSession.PendingPhysicalDraws(side) == 1, "Draw spell must consume its hand slot before drawing");
            ConfirmAll(side);
            Check(!VRGameSession.TryCastPhysicalSpell(spell, side) && VRGameSession.PhysicalHandCount(side) == 12, "Repeated spell consumed hand");
            UnityEngine.Object.DestroyImmediate(spell);

            EmptyHand(side);
            game.DrawCards(side, 6); ConfirmAll(side); EmptyHand(side);
            Check(VRGameSession.RemainingPhysicalDeck(side) == 1, "Deck count after 19 successful draws incorrect");
            game.DrawCards(side, 3);
            Check(game.Board.me.HeroHealth == 25 && VRGameSession.PendingPhysicalDraws(side) == 1, "Fatigue applied before last real card confirmation");
            ConfirmAll(side);
            Check(VRGameSession.ConfirmedPhysicalDraws(side) == 20 && VRGameSession.PhysicalHandCount(side) == 1 &&
                game.Board.me.fatigueDamage == 2 && game.Board.me.HeroHealth == 22, "Mixed draw must grant one card then fatigue 1+2");
            game.DrawCards(side, 1);
            Check(game.Board.me.HeroHealth == 19 && VRGameSession.ConfirmedPhysicalDraws(side) == 20, "Empty draw incorrectly counted as successful");
            ConfirmAll(PlayerSide.Opponent);
            Check(VRGameSession.PhysicalHandCount(PlayerSide.Opponent) == 6 && game.Board.opponent.fatigueDamage == 0, "Opponent tracking contaminated");
            VRGameSession.ResetGame();
            Check(VRGameSession.ConfirmedPhysicalDraws(side) == 0 && VRGameSession.PhysicalHandCount(side) == 0 &&
                VRGameSession.RemainingPhysicalDeck(side) == 20 && VRGameSession.Game.Board.me.fatigueDamage == 0, "Rematch did not reset draw state");
            Debug.Log("PHYSICAL_DRAW_VERIFICATION_PASS: opening/turn allocation, confirmed deck-to-hand transfer, hand cap, spell consumption, mixed draws, fatigue, side isolation, reset");
            var mundo = UnityEditor.AssetDatabase.LoadAssetAtPath<CardData>("Assets/2. Data/Card/18_R.asset");
            Check(mundo.attack == 1 && mundo.health == 3 && mundo.HasKeyword(CardKeyword.FrontDamage), "Mundo must be 1/3 with front damage");
            var target = ScriptableObject.CreateInstance<CardData>(); target.attack = 5; target.health = 4;
            VRGameSession.Game.Board.opponent.lanes[0] = new CardInstance(target, PlayerSide.Opponent);
            VRGameSession.Game.ApplyFrontDamage(side, 0, mundo.attack);
            Check(VRGameSession.Game.Board.opponent.lanes[0].currentHealth == 3, "Mundo front damage must equal attack 1");
            UnityEngine.Object.DestroyImmediate(target); VRGameSession.ResetGame();
            Debug.Log("MUNDO_1_3_FRONT_DAMAGE_1_PASS");
        }
        public static void CompleteChecks()
        {
            Run();
            ModernRuleVerification.Verify();
            ProductionGameVerification.Run();
        }
    }
}

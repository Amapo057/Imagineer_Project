using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KJH.Balancing
{
    public static class AppliedBalanceVerification
    {
        [Serializable] public class Row { public int id, cost, attack, health, keywords; public string name, assetPath; }
        [Serializable] public class Snapshot { public Row[] cards; }
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        public static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            var rows = JsonUtility.FromJson<Snapshot>(File.ReadAllText(args[Array.IndexOf(args, "-balanceSheetSnapshot") + 1])).cards;
            var database = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
            Check(rows.Length == 27, "Expected 27 spreadsheet rows");
            foreach (var row in rows)
            {
                var card = database.GetCardById(row.id);
                Check(card != null && AssetDatabase.GetAssetPath(card) == row.assetPath && card.cardName.EndsWith(row.name), "Card mapping changed: " + row.id);
                Check(card.cost == row.cost && card.attack == row.attack && card.health == row.health &&
                    (int)card.keywords == row.keywords, "Spreadsheet balance mismatch: " + row.id);
            }
            var rules = new MatchRuleSet();
            Check(rules.heroHealth == 25 && rules.openingHand == 6 && rules.cardsPerTurn == 1 && rules.maxHandSize == 12 &&
                rules.mutualMinionDamage && rules.permanentBuff && rules.coinExceedsCap && rules.fatigueOnEmptyDraw &&
                rules.healSpellAmount == 4 && rules.drawSpellCards == 3 && rules.removeSpellDamage == 4 &&
                rules.globalClear && rules.buffSpellAttack == 3, "Spreadsheet match rules mismatch");
            var target = ScriptableObject.CreateInstance<CardData>(); target.attack = 9; target.health = 20;
            try
            {
                foreach (int id in new[] { 8, 15, 19, 9, 11, 18 })
                {
                    var card = database.GetCardById(id);
                    var game = new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false });
                    game.StartGame(PlayerSide.Me, dealCards: false);
                    game.Board.me.lanes[0] = new CardInstance(card, PlayerSide.Me);
                    game.Board.opponent.lanes[0] = new CardInstance(target, PlayerSide.Opponent);
                    if (card.HasKeyword(CardKeyword.Weapon))
                    {
                        var weapons = new ProductionWeaponState(); weapons.Arm(game, PlayerSide.Me, card);
                        Check(weapons.Strike(game, PlayerSide.Me, id, 0, game.TurnNumber), "Weapon rejected: " + id);
                        Check(!weapons.Strike(game, PlayerSide.Me, id, 0, game.TurnNumber), "Weapon used twice: " + id);
                    }
                    else game.ApplyFrontDamage(PlayerSide.Me, 0, card.attack);
                    Check(game.Board.opponent.lanes[0].currentHealth == 20 - card.attack, "Effect damage must equal attack: " + id);
                    Check(game.Board.me.lanes[0].currentHealth == card.health, "Effect incorrectly took retaliation damage: " + id);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
            Debug.Log("APPLIED_BALANCE_SHEET_PASS: all 27 live cards match spreadsheet; rules match; weapons 3/3/2 and front damage 2/2/1; no effect retaliation");
            AutomaticDrawVerification.Run();
            ModernRuleVerification.Verify();
            ProductionWeaponVerification.Verify();
            ProductionGameVerification.Run();
            Debug.Log("APPLIED_BALANCE_INTEGRATION_PASS");
        }
    }
}

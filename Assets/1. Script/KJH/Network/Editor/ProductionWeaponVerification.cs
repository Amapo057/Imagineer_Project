using System;
using UnityEditor;
using UnityEngine;

public static class ProductionWeaponVerification
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static void CompleteChecks()
    {
        Verify();
        ProductionGameVerification.CompleteChecks();
    }
    public static void Verify()
    {
        var cards = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
        var game = new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false }); game.StartGame(PlayerSide.Me, dealCards:false);
        var weapons = new ProductionWeaponState();
        weapons.Arm(game,PlayerSide.Me,cards.GetCardById(15));
        Check(weapons.Pending(game,PlayerSide.Me)==0,"Empty enemy board granted weapon");
        game.Board.opponent.lanes[0]=new CardInstance(cards.GetCardById(5),PlayerSide.Opponent);
        weapons.Arm(game,PlayerSide.Me,cards.GetCardById(19));
        Check(!weapons.Strike(game,PlayerSide.Me,19,-1,game.TurnNumber),"Invalid lane accepted");
        Check(!weapons.Strike(game,PlayerSide.Me,19,1,game.TurnNumber),"Empty lane consumed weapon");
        Check(!weapons.Strike(game,PlayerSide.Opponent,19,0,game.TurnNumber),"Opponent turn accepted");
        game.EndTurn();game.EndTurn();
        Check(weapons.Pending(game,PlayerSide.Me)==0,"Charge survived end turn");
        weapons.Arm(game,PlayerSide.Me,cards.GetCardById(19));
        Check(weapons.Pending(game,PlayerSide.Me)==0,"Same weapon card armed twice");
        var next = new GameManager(false,new MatchRuleSet {fatigueOnEmptyDraw=false});next.StartGame(PlayerSide.Me,dealCards:false);
        next.Board.opponent.lanes[0]=new CardInstance(cards.GetCardById(5),PlayerSide.Opponent);
        weapons.Arm(next,PlayerSide.Me,cards.GetCardById(15));
        Check(weapons.Strike(next,PlayerSide.Me,15,0,next.TurnNumber),"Fresh match weapon rejected");
        Check(next.Board.opponent.lanes[0].currentHealth==cards.GetCardById(5).health-cards.GetCardById(15).attack,"Weapon damage must equal card attack");
        var slot=new GameObject("Weapon geometry test");slot.transform.SetPositionAndRotation(new Vector3(3,2,1),Quaternion.Euler(20,70,15));
        Check(ProductionWeaponInput.Contains(slot.transform,slot.transform.position+slot.transform.up*.1f),"Rotated slot hit failed");
        Check(!ProductionWeaponInput.Contains(slot.transform,slot.transform.position+slot.transform.right*.2f),"Adjacent lane accepted");
        UnityEngine.Object.DestroyImmediate(slot);
        Debug.Log("PRODUCTION_WEAPON_RULES_PASS: empty target, turn expiry, duplicate arm, rematch, rotated target volume");
    }
}

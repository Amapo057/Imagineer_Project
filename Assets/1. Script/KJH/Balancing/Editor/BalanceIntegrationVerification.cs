using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KJH.Balancing
{
    public static class BalanceIntegrationVerification
    {
        public static void CompleteChecks()
        {
            Verify();
            BalanceSimulation.RunBatch();
        }
        static void Require(bool value,string message) { if(!value)throw new Exception(message); }

        public static void Verify()
        {
            var a=new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false });var b=new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false });
            a.StartGame(PlayerSide.Me);b.StartGame(PlayerSide.Opponent);
            var coinA=new SecondPlayerCoin(a);var coinB=new SecondPlayerCoin(b);
            Require(!coinA.IsAvailable(PlayerSide.Me) && coinB.IsAvailable(PlayerSide.Me),"Only second player gets a coin");
            Require(!coinB.TryUse(PlayerSide.Me),"Cannot use coin outside own turn");
            a.EndTurn();b.EndTurn();
            Require(coinB.TryUse(PlayerSide.Me),"Second player can use coin");
            Require(b.Board.me.currentCost==3 && b.Board.me.maxCost==2 && coinB.DisplayCapacity(PlayerSide.Me)==3,"Coin must grant temporary spendable cost");
            Require(!coinB.TryUse(PlayerSide.Me),"Coin cannot be used twice");

            var go=new GameObject("Coin network dispatch verification");
            var net=go.AddComponent<GameNetworkManager>();
            int applied=0;
            net.OnCoinUsedReceived+=()=>{if(coinA.TryUse(PlayerSide.Opponent))applied++;};
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var line=typeof(GameNetworkManager).GetMethod("HandleLine",flags);
            var update=typeof(GameNetworkManager).GetMethod("Update",flags);
            line.Invoke(net,new object[]{NetMsg.CoinUsed});
            line.Invoke(net,new object[]{NetMsg.CoinUsed});
            update.Invoke(net,null);
            Require(applied==1 && a.Board.opponent.currentCost==b.Board.me.currentCost,"Network coin must apply once on mirrored board");
            UnityEngine.Object.DestroyImmediate(go);
            a.EndTurn();b.EndTurn();a.EndTurn();b.EndTurn();
            Require(b.Board.me.currentCost==2 && coinB.DisplayCapacity(PlayerSide.Me)==2,"Bonus must expire on next own turn");
            Require(!coinB.IsAvailable(PlayerSide.Me),"Spent coin must stay spent");

            var capped=new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false });capped.StartGame(PlayerSide.Me);capped.EndTurn();
            var capCoin=new SecondPlayerCoin(capped);capped.Board.opponent.currentCost=GameRules.MaxCost;
            Require(capCoin.TryUse(PlayerSide.Opponent) && capped.Board.opponent.currentCost==9,"Coin may exceed base cap by one");
            Require(!capCoin.TryUse(PlayerSide.Opponent),"Coin still works only once above base cap");
            capped.Board.opponent.maxCost=8;
            Require(capCoin.DisplayCapacity(PlayerSide.Opponent)==9,"HUD capacity must include temporary coin above base cap");
            capped.Board.me.faceHitCount=GameRules.FaceHitThreshold;capped.CheckGameOver();
            Require(!capCoin.TryUse(PlayerSide.Opponent),"No coin use after game over");

            VRGameSession.ResetGame();VRGameSession.ApplyNetworkFirstPlayer(PlayerSide.Opponent);
            VRGameSession.Game.EndTurn();
            Require(VRGameSession.Coin.TryUse(PlayerSide.Me),"VR session exposes second-player coin");
            VRGameSession.ResetGame();VRGameSession.ApplyNetworkFirstPlayer(PlayerSide.Opponent);
            Require(VRGameSession.Coin.IsAvailable(PlayerSide.Me),"Rematch replenishes coin");
            VRGameSession.ResetGame();
            Debug.Log("BALANCE_COIN_INTEGRATION_VERIFIED: owner, turn, once-only, temporary cost, cap, network dispatch, mirrored state, rematch");

            var args=Environment.GetCommandLineArgs();string work=args[Array.IndexOf(args,"-balanceWork")+1];
            string json="{\"cards\":"+File.ReadAllText(Path.Combine(work,"proposed_stats.json"))+"}";
            var expected=JsonUtility.FromJson<BalanceSimulation.Trial>(json);
            foreach(var stat in expected.cards)
            {
                var card=AssetDatabase.FindAssets("t:CardData",new[]{"Assets/2. Data/Card"})
                    .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<CardData>).Single(c=>c.cardId==stat.id);
                Require(card.cost==stat.cost && card.attack==stat.attack && card.health==stat.health,"Card asset mismatch "+stat.id);
            }
            Debug.Log("BALANCE_CARD_ASSETS_VERIFIED: 27 cards match proposed stats");
            var database=AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
            Require(database.GetCardById(2).HasKeyword(CardKeyword.Draw),"Slime must draw one");
            foreach(int id in new[]{6,12,22})Require(database.GetCardById(id).keywords==CardKeyword.None,"Removed heal remains on card "+id);
            Require(database.GetCardById(24).keywords==CardKeyword.Draw&&
                database.GetCardById(25).keywords==(CardKeyword.Heal|CardKeyword.Draw)&&
                database.GetCardById(26).keywords==CardKeyword.Remove&&
                database.GetCardById(27).keywords==CardKeyword.Buff,"Spell keyword assets mismatch");
            Debug.Log("BALANCE_CARD_KEYWORDS_VERIFIED: draw slime, removed unit heals, four spell effects");
        }
    }
}

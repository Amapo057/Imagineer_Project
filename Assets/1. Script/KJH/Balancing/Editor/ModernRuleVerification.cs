using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KJH.Balancing
{
    public static class ModernRuleVerification
    {
        public static void CompleteChecks()
        {
            Verify();
            BalanceIntegrationVerification.Verify();
            LocalNetworkVerification.Run();
        }
        public static void FinalizeV2()
        {
            CompleteChecks();
            BalanceSimulation.RunBatch();
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        static CardData Card(int attack=1,int health=2)
        {
            var c=ScriptableObject.CreateInstance<CardData>();c.attack=attack;c.health=health;c.cost=1;return c;
        }
        static GameManager Isolated()
        {
            var g=new GameManager(false,new MatchRuleSet{openingHand=0,cardsPerTurn=0,fatigueOnEmptyDraw=false});
            g.StartGame(PlayerSide.Me);return g;
        }
        public static void Verify()
        {
            VerifyMutualCombat();
            var body=Card();var g=new GameManager();int drawn=0;g.OnCardDrawn+=(s,c)=>drawn++;
            for(int i=0;i<20;i++){g.Board.me.deck.Add(body);g.Board.opponent.deck.Add(body);}
            g.StartGame(PlayerSide.Me);
            Check(g.Board.me.hand.Count==7&&g.Board.opponent.hand.Count==6&&drawn==13,"Opening 6 + automatic 1 draw incorrect");
            g.EndTurn();
            Check(g.Board.opponent.hand.Count==7&&drawn==14,"Turn draw incorrect");
            Check(g.Board.me.HeroHealth==25&&g.Board.opponent.HeroHealth==25,"Initial HP must be 25");
            g.DrawCards(PlayerSide.Me,20);
            Check(g.Board.me.hand.Count==12&&g.Board.me.deck.Count==8,"Hand cap must be 12 without discarding deck cards");

            foreach(int attack in new[]{1,7})
            {
                var game=Isolated();game.EndTurn();var unit=Card(attack,3);
                game.Board.opponent.lanes[0]=new CardInstance(unit,PlayerSide.Opponent);game.EndTurn();
                Check(game.Board.me.HeroHealth==25-attack,"Hero must take attack damage");
                UnityEngine.Object.DestroyImmediate(unit);
            }

            var draw=Card();draw.cardType=CardType.Spell;draw.keywords=CardKeyword.Draw;
            var spellGame=Isolated();spellGame.Board.me.currentCost=8;
            spellGame.Board.me.hand.Add(draw);
            for(int i=0;i<6;i++)spellGame.Board.me.deck.Add(body);
            string sequence="";spellGame.OnCardDrawn+=(s,c)=>sequence+="D";
            Check(spellGame.TryCastSpell(draw,PlayerSide.Me,onConsumed:()=>sequence+="C"),"Draw spell rejected");
            Check(spellGame.Board.me.hand.Count==3&&sequence=="CDDD","Spell draws three after consuming card");
            var heal=Card();heal.cardType=CardType.Spell;heal.keywords=CardKeyword.Heal|CardKeyword.Draw;
            spellGame.Board.me.heroDamage=10;spellGame.Board.me.hand.Add(heal);
            Check(spellGame.TryCastSpell(heal,PlayerSide.Me)&&spellGame.Board.me.HeroHealth==19&&spellGame.Board.me.hand.Count==4,"Heal four + draw one incorrect");
            spellGame.ReduceFaceHitCount(PlayerSide.Me,99);
            Check(spellGame.Board.me.HeroHealth==25,"Heal must cap at starting health");

            var clear=Card();clear.cardType=CardType.Spell;clear.keywords=CardKeyword.Remove;
            var field=Isolated();field.Board.me.currentCost=8;field.Board.me.hand.Add(clear);
            var big=Card(2,8);
            foreach(var side in new[]{PlayerSide.Me,PlayerSide.Opponent})
            {
                var b=field.Board.GetBoard(side);
                for(int lane=0;lane<GameRules.LaneCount;lane++)b.lanes[lane]=new CardInstance(lane%2==0?body:big,side);
                b.heroDamage=4;
            }
            Check(field.TryCastSpell(clear,PlayerSide.Me),"Global spell should need no selected target");
            foreach(var b in new[]{field.Board.me,field.Board.opponent})
            {
                for(int lane=0;lane<GameRules.LaneCount;lane++)
                    Check(lane%2==0?b.lanes[lane]==null:b.lanes[lane]?.currentHealth==4,"Global spell must damage every lane on both fields by 4");
                Check(b.HeroHealth==21,"Global spell must never damage heroes");
            }

            var buffGame=Isolated();var buff=Card();buff.cardType=CardType.Spell;buff.keywords=CardKeyword.Buff;
            buffGame.Board.me.currentCost=8;buffGame.Board.me.hand.Add(buff);
            buffGame.Board.me.lanes[0]=new CardInstance(big,PlayerSide.Me);
            Check(buffGame.TryCastSpell(buff,PlayerSide.Me),"Buff spell rejected");
            buffGame.EndTurn();buffGame.EndTurn();
            Check(buffGame.Board.me.lanes[0].currentAttack==big.attack+3&&buffGame.Board.me.lanes[0].tempAttackBonus==0,"Buff must survive turn changes");
            buffGame.Board.me.lanes[1]=new CardInstance(big,PlayerSide.Me);
            Check(buffGame.Board.me.lanes[1].currentAttack==big.attack,"Future summons must not inherit buff");

            var fatigue=new GameManager(false,new MatchRuleSet{openingHand=0,cardsPerTurn=0});fatigue.StartGame(PlayerSide.Me);
            fatigue.DrawCards(PlayerSide.Me,2);
            Check(fatigue.Board.me.HeroHealth==22&&fatigue.Board.me.fatigueDamage==2,"Empty deck draw should deal fatigue 1 then 2");
            fatigue.DrawCards(PlayerSide.Me,2);
            Check(fatigue.Board.me.HeroHealth==15&&fatigue.Board.me.fatigueDamage==4,"Fatigue must escalate per attempted card");

            VRGameSession.ResetGame();
            Check(VRGameSession.Game.Board.me.HeroHealth==25&&VRGameSession.PendingPhysicalDraws(PlayerSide.Me)==7,"Physical deck opening allocation incorrect");
            VRGameSession.Game.DrawCards(PlayerSide.Me,14);
            Check(VRGameSession.Game.Board.me.HeroHealth==25,"Unconfirmed physical draws must not cause fatigue");
            for(int i=0;i<12;i++)VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me);
            Check(VRGameSession.PhysicalHandCount(PlayerSide.Me)==12,"Physical hand cap missing");
            for(int i=0;i<12;i++)VRGameSession.ConsumePhysicalCard(PlayerSide.Me);
            VRGameSession.Game.DrawCards(PlayerSide.Me,9);
            for(int i=0;i<8;i++)VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me);
            Check(VRGameSession.Game.Board.me.HeroHealth==24,"Physical draw requests beyond 20 must produce fatigue");
            VRGameSession.ResetGame();
            VerifyPhysicalSpellInput();
            Debug.Log("MODERN_RULE_VERIFICATION_SUCCESS: HP25, opening6, draw1, hand12, drawSpell3, heal4, clearAllEight4, permanentBuff, fatigue, physical quota");
            foreach(var c in new[]{body,draw,heal,clear,big,buff})UnityEngine.Object.DestroyImmediate(c);
        }

        static void VerifyMutualCombat()
        {
            foreach (bool counter in new[] { false, true })
            foreach (int attackHealth in new[] { 2, 5 })
            foreach (int defendHealth in new[] { 2, 4 })
            {
                var game = Isolated();
                var attackData = Card(3, attackHealth);
                var defendData = Card(2, defendHealth);
                defendData.keywords = counter ? CardKeyword.Counter : CardKeyword.None;
                var attack = new CardInstance(attackData, PlayerSide.Opponent);
                var defend = new CardInstance(defendData, PlayerSide.Me);
                game.Board.opponent.lanes[0] = attack; game.Board.me.lanes[0] = defend;
                string events = "";
                game.OnMinionAttacked += (side, lane) => events += "A";
                game.OnMinionHit += (side, lane) =>
                {
                    Check(attack.currentHealth == attackHealth - 2 && defend.currentHealth == defendHealth - 3,
                        "Both combat damages must apply before hit events");
                    events += side == PlayerSide.Me ? "D" : "R";
                    defend.currentAttack = 100; // Hit listeners cannot change this exchange's damage.
                };
                game.EndTurn();
                Check(events == "" && attack.currentHealth == attackHealth && defend.currentHealth == defendHealth,
                    "First turn must not attack");
                game.EndTurn();
                Check(events == "ADR", "Normal combat must notify attack, defender hit and attacker hit once each");
                Check((game.Board.me.lanes[0] != null) == (defendHealth > 3) &&
                    (game.Board.opponent.lanes[0] != null) == (attackHealth > 2), "Mutual combat death cleanup mismatch");
                Check(game.Board.me.HeroHealth == 25 && game.Board.opponent.HeroHealth == 25,
                    "Minion combat must not damage heroes");
                UnityEngine.Object.DestroyImmediate(attackData); UnityEngine.Object.DestroyImmediate(defendData);
            }
            Check(!MatchRuleSet.Legacy().mutualMinionDamage, "Legacy comparisons must retain old combat");
            Debug.Log("MUTUAL_MINION_COMBAT_VERIFIED: ordinary and Counter defenders, lethal retaliation, simultaneous deaths, event ordering, first-turn guard");
        }

        static void VerifyPhysicalSpellInput()
        {
            while(VRGameSession.ConfirmPhysicalDraw(PlayerSide.Me)) { }
            var game=VRGameSession.Game;game.Board.me.currentCost=8;game.Board.me.heroDamage=10;
            var body=Card(2,8);
            game.Board.me.lanes[0]=new CardInstance(body,PlayerSide.Me);
            game.Board.opponent.lanes[0]=new CardInstance(body,PlayerSide.Opponent);
            var go=new GameObject("Physical spell input verification");go.SetActive(false);
            var slot=go.AddComponent<SlotModelSpawner>();
            var visual=new GameObject("Existing friendly model");visual.transform.SetParent(go.transform);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string field,object value)=>typeof(SlotModelSpawner).GetField(field,flags).SetValue(slot,value);
            Set("cardDatabase",AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset"));
            Set("side",PlayerSide.Me);Set("laneIndex",0);Set("confirmedCardId",1);Set("spawnedModel",visual);
            var confirm=typeof(SlotModelSpawner).GetMethod("Confirm",flags);
            // Provide an inactive network fixture; Instance must not auto-create a persistent object in EditMode.
            var networkObject=new GameObject("Physical spell network fixture");networkObject.SetActive(false);
            var network=networkObject.AddComponent<GameNetworkManager>();
            var instanceField=typeof(GameNetworkManager).GetField("instance",BindingFlags.Static|BindingFlags.NonPublic);
            var previous=instanceField.GetValue(null);instanceField.SetValue(null,network);
            confirm.Invoke(slot,new object[]{25});
            Check(game.Board.me.HeroHealth==19&&game.Board.me.currentCost==7,"Marker-confirmed heal spell failed");
            Check((int)typeof(SlotModelSpawner).GetField("confirmedCardId",flags).GetValue(slot)==1&&visual!=null,"Spell replaced friendly model");
            confirm.Invoke(slot,new object[]{25});
            Check(game.Board.me.currentCost==7,"Repeated spell detection consumed twice");
            confirm.Invoke(slot,new object[]{26});
            Check(game.Board.me.lanes[0].currentHealth==4&&game.Board.opponent.lanes[0].currentHealth==4,"Marker-confirmed global spell failed");
            UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(body);
            UnityEngine.Object.DestroyImmediate(networkObject);instanceField.SetValue(null,previous);
            VRGameSession.ResetGame();
            Debug.Log("MODERN_PHYSICAL_SPELL_INPUT_VERIFIED: existing slot confirmation routes spells, preserves unit visual, rejects duplicates");
        }

    }
}

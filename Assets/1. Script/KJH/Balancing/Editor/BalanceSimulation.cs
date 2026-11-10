using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KJH.Balancing
{
    // Combat, costs, draws and win detection use production GameManager with match-local rules.
    // Hand actions mirror DemoTurnController; bots never inspect the opponent hand/deck.
    public static class BalanceSimulation
    {
        [Serializable] public class Stat { public int id, cost, attack, health; }
        [Serializable] public class Trial
        {
            public string name;
            public int pairs = 64, seed = 31000, opening = 5;
            public int secondFirstTurnBonus;
            public int firstOpeningReduction;
            public bool coin;
            public bool modern, sweep, healDraw;
            public KeywordSnapshot[] keywordOverrides = Array.Empty<KeywordSnapshot>();
            public MatchRuleSet rules;
            public int opponentPolicyOffset, mirrorFaction;
            public bool limitedVR;
            public bool disableWeaponBattlecry;
            public int weaponDamageCap, frontDamageCap;
            public Stat[] cards = Array.Empty<Stat>();
        }
        [Serializable] public class Batch { public Trial[] trials; }
        [Serializable] public class KeywordSnapshot { public int id, keywords; }
        [Serializable] public class KeywordSnapshots { public KeywordSnapshot[] cards; }
        [Serializable] public class CardMetric
        {
            public int id, drawn, played, attacks, faceHits, openingGames, openingWins;
        }
        [Serializable] public class Result
        {
            public string name;
            public int games, humanWins, demonWins, firstWins, secondWins, draws, cutoffs, totalTurns;
            public int coinsUsed, coinTurnTotal;
            public int lowHandTurns, noActionTurns, behindSeatGames, comebackWins;
            public int[] policyGames = new int[3], policyHumanWins = new int[3], policyFirstWins = new int[3];
            public CardMetric[] cards = Enumerable.Range(1,27).Select(i => new CardMetric { id=i }).ToArray();
            public List<GameMetric> matches=new List<GameMetric>();
        }
        [Serializable] public class GameMetric { public int seed, policy, starter, winner, turns; }
        struct Move { public int hand, lane, target; }
        static readonly FieldInfo TurnField = typeof(GameManager).GetField("turnNumber", BindingFlags.NonPublic | BindingFlags.Instance);

        public static void RunBatch()
        {
            VerifyRules();
            var args = Environment.GetCommandLineArgs();
            string folder = args[Array.IndexOf(args,"-balanceWork")+1];
            var batch = JsonUtility.FromJson<Batch>(File.ReadAllText(Path.Combine(folder,"batch.json")));
            var baseline = JsonUtility.FromJson<Trial>("{\"cards\":"+File.ReadAllText(Path.Combine(folder,"baseline_stats.json"))+"}").cards;
            var keywords = JsonUtility.FromJson<KeywordSnapshots>(File.ReadAllText(Path.Combine(folder,"baseline_keywords.json"))).cards;
            var source = AssetDatabase.FindAssets("t:CardData",new[]{"Assets/2. Data/Card"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<CardData>).OrderBy(c=>c.cardId).ToArray();
            if(source.Length!=27) throw new Exception("Expected 27 cards");
            foreach(var trial in batch.trials)
            {
                var cards=source.Select(UnityEngine.Object.Instantiate).ToArray();
                foreach(var stat in baseline)
                {
                    var card=cards.Single(c=>c.cardId==stat.id);
                    card.cost=stat.cost;card.attack=stat.attack;card.health=stat.health;
                }
                foreach(var item in keywords)cards.Single(c=>c.cardId==item.id).keywords=(CardKeyword)item.keywords;
                foreach(var stat in trial.cards)
                {
                    var card=cards.Single(c=>c.cardId==stat.id);
                    card.cost=stat.cost; card.attack=stat.attack; card.health=stat.health;
                }
                if(trial.sweep) cards.Single(c=>c.cardId==27).keywords=CardKeyword.Cleave;
                if(trial.healDraw) cards.Single(c=>c.cardId==25).keywords=CardKeyword.Heal|CardKeyword.Draw;
                foreach(var item in trial.keywordOverrides) cards.Single(c=>c.cardId==item.id).keywords=(CardKeyword)item.keywords;
                var result=Run(trial,cards);
                File.WriteAllText(Path.Combine(folder,trial.name+".json"),JsonUtility.ToJson(result,true));
                Debug.Log($"BALANCE_RESULT {trial.name}: games={result.games} human={result.humanWins} demon={result.demonWins} first={result.firstWins} draws={result.draws} cutoff={result.cutoffs} meanTurns={(double)result.totalTurns/result.games:F2}");
                foreach(var card in cards) UnityEngine.Object.DestroyImmediate(card);
            }
            Debug.Log("BALANCE_BATCH_COMPLETE");
        }

        static void VerifyRules()
        {
            var attacker=ScriptableObject.CreateInstance<CardData>();
            var defender=ScriptableObject.CreateInstance<CardData>();
            attacker.attack=7;attacker.health=4;defender.attack=2;defender.health=2;
            foreach(bool counter in new[]{false,true})
            {
                defender.keywords=counter?CardKeyword.Counter:CardKeyword.None;
                var game=new GameManager(false,MatchRuleSet.Legacy());game.StartGame(PlayerSide.Me);
                if(game.Board.me.currentCost!=1)throw new Exception("First cost mismatch");
                game.Board.me.lanes[0]=new CardInstance(attacker,PlayerSide.Me);
                game.EndTurn();
                if(game.Board.opponent.faceHitCount!=0 || game.Board.opponent.currentCost!=2)
                    throw new Exception("First turn attack / second cost mismatch");
                game.Board.me.lanes[0]=new CardInstance(defender,PlayerSide.Me);
                game.Board.opponent.lanes[0]=new CardInstance(attacker,PlayerSide.Opponent);
                game.EndTurn();
                if(game.Board.me.lanes[0]!=null || game.Board.opponent.lanes[0].currentHealth!=(counter?2:4))
                    throw new Exception("Counter/non-counter combat mismatch");
            }
            foreach(int attack in new[]{1,7})
            {
                attacker.attack=attack;
                var game=new GameManager(false,MatchRuleSet.Legacy());game.StartGame(PlayerSide.Me);game.EndTurn();
                game.Board.opponent.lanes[0]=new CardInstance(attacker,PlayerSide.Opponent);
                game.EndTurn();
                if(game.Board.me.faceHitCount!=1)throw new Exception("Face hit must be one regardless of attack");
            }
            UnityEngine.Object.DestroyImmediate(attacker);UnityEngine.Object.DestroyImmediate(defender);
            Debug.Log("BALANCE_RULE_CHECKS_PASSED");
        }

        static bool simulateWeaponBattlecry;
        static int weaponDamageCap, frontDamageCap;
        static int EffectDamage(int attack, int cap) => cap > 0 ? Math.Min(attack, cap) : attack;
        static Result Run(Trial trial, CardData[] cards)
        {
            simulateWeaponBattlecry = !trial.disableWeaponBattlecry;
            weaponDamageCap = trial.weaponDamageCap;
            frontDamageCap = trial.frontDamageCap;
            var result=new Result{name=trial.name};
            for(int policy=0;policy<3;policy++)
            for(int pair=0;pair<trial.pairs;pair++)
            for(int starter=0;starter<2;starter++)
            {
                int seed=trial.seed+pair*101+policy*100003;
                Match(trial,cards,policy,seed,(PlayerSide)starter,result);
            }
            return result;
        }

        static void Match(Trial trial,CardData[] cards,int policy,int seed,PlayerSide starter,Result metrics)
        {
            var game=new GameManager(resourceExhaustionDrawEnabled:!trial.modern,
                rules:trial.modern?(trial.rules??new MatchRuleSet()):MatchRuleSet.Legacy());
            var human=cards.Where(c=>c.cardId<=16 || c.cardId>=24).ToList();
            var demon=cards.Where(c=>c.cardId<=9 || c.cardId>=17).ToList();
            if(trial.mirrorFaction==1)demon=human.ToList();
            if(trial.mirrorFaction==2)human=demon.ToList();
            Shuffle(human,new System.Random(seed)); Shuffle(demon,new System.Random(seed+37));
            game.Board.me.deck.AddRange(human);game.Board.opponent.deck.AddRange(demon);
            game.OnCardDrawn+=(side,card)=>metrics.cards[card.cardId-1].drawn++;
            if(!trial.modern)
                for(int i=0;i<trial.opening;i++) { Draw(game,PlayerSide.Me,metrics);Draw(game,PlayerSide.Opponent,metrics); }
            PlayerSide? winner=null;
            game.OnGameOver+=side=>winner=side;
            int attackerId=0;
            game.OnMinionAttacked+=(side,lane)=>
            {
                attackerId=game.Board.GetBoard(side).lanes[lane].data.cardId;
                metrics.cards[attackerId-1].attacks++;
            };
            game.OnFaceHit+=side=> { if(attackerId>0) metrics.cards[attackerId-1].faceHits++; };
            game.StartGame(starter);
            // Simulation-only opening adjustment: undo surplus draws from the end of the first hand.
            // Returning them to the front preserves the exact deck order for subsequent draws.
            if (trial.modern && trial.firstOpeningReduction > 0)
            {
                if (trial.firstOpeningReduction >= game.Rules.openingHand) throw new Exception("Invalid first opening reduction");
                var first = game.Board.GetBoard(starter);
                for (int n = 0; n < trial.firstOpeningReduction; n++)
                {
                    int last = first.hand.Count - 1;
                    var returned = first.hand[last]; first.hand.RemoveAt(last); first.deck.Insert(0, returned);
                    metrics.cards[returned.cardId - 1].drawn--;
                }
                if (first.hand.Count != game.Rules.openingHand - trial.firstOpeningReduction + game.Rules.cardsPerTurn)
                    throw new Exception("First opening hand/draw mismatch");
            }
            int openingCount=trial.modern?game.Rules.openingHand:trial.opening;
            var opening=game.Board.me.hand.Take(openingCount-(trial.modern&&starter==PlayerSide.Me?trial.firstOpeningReduction:0))
                .Concat(game.Board.opponent.hand.Take(openingCount-(trial.modern&&starter==PlayerSide.Opponent?trial.firstOpeningReduction:0)))
                .Select(c=>c.cardId).ToArray();
            var coin=new SecondPlayerCoin(game);
            var noise=new System.Random(seed+7919+(int)starter*97);
            int turns=0;
            var behind=new bool[2];
            while(!game.IsGameOver && turns<100)
            {
                var side=game.State.CurrentPlayer;
                var active=game.Board.GetBoard(side);
                if(active.isSecondPlayer && active.ownTurnCount==1)
                {
                    active.currentCost+=trial.secondFirstTurnBonus;
                    active.maxCost+=trial.secondFirstTurnBonus;
                }
                if(!trial.modern) Draw(game,side,metrics);
                int activePolicy=side==PlayerSide.Me?policy:(policy+trial.opponentPolicyOffset)%3;
                if(trial.coin && coin.CanUse(side) && ShouldCoin(game,activePolicy,trial.limitedVR))
                {
                    if(!coin.TryUse(side))throw new Exception("Coin use disagrees with simulation");
                    metrics.coinsUsed++;metrics.coinTurnTotal+=active.ownTurnCount;
                }
                int actionCount=0;
                for(int actions=0;actions<20 && !game.IsGameOver;actions++)
                {
                    var moves=Legal(game,trial.limitedVR);
                    if(moves.Count==0)break;
                    double best=Forecast(game,side,activePolicy);
                    Move? chosen=null;
                    foreach(var move in moves)
                    {
                        var copy=Clone(game);
                        Apply(copy,move,trial.limitedVR);
                        double score=Forecast(copy,side,activePolicy)+(noise.NextDouble()-0.5)*0.18;
                        if(score>best+0.015){best=score;chosen=move;}
                    }
                    if(!chosen.HasValue) break;
                    var data=game.Board.GetBoard(side).hand[chosen.Value.hand];
                    metrics.cards[data.cardId-1].played++;
                    Apply(game,chosen.Value,trial.limitedVR);
                    actionCount++;
                }
                if(actionCount==0)metrics.noActionTurns++;
                if(game.Board.GetBoard(side).hand.Count<=1)metrics.lowHandTurns++;
                if(!game.IsGameOver)game.EndTurn();
                int ownCount=game.Board.me.lanes.Count(c=>c!=null);
                int enemyCount=game.Board.opponent.lanes.Count(c=>c!=null);
                behind[0]|=enemyCount>=ownCount+2;
                behind[1]|=ownCount>=enemyCount+2;
                turns++;
            }
            metrics.games++; metrics.totalTurns+=turns;metrics.policyGames[policy]++;
            metrics.matches.Add(new GameMetric{seed=seed,policy=policy,starter=(int)starter,winner=winner.HasValue?(int)winner.Value:-1,turns=turns});
            metrics.behindSeatGames+=behind.Count(x=>x);
            if(winner.HasValue && behind[(int)winner.Value])metrics.comebackWins++;
            if(!game.IsGameOver){metrics.cutoffs++;}
            else if(!winner.HasValue){metrics.draws++;}
            else
            {
                if(winner==PlayerSide.Me){metrics.humanWins++;metrics.policyHumanWins[policy]++;}
                else metrics.demonWins++;
                if(winner==starter){metrics.firstWins++;metrics.policyFirstWins[policy]++;}
                else metrics.secondWins++;
            }
            foreach(int id in opening)
            {
                metrics.cards[id-1].openingGames++;
                bool won=(id>=10 && id<=16 && winner==PlayerSide.Me)||(id>=17 && id<=23 && winner==PlayerSide.Opponent);
                // Shared cards are excluded from faction-specific opening-win comparisons.
                if(won)metrics.cards[id-1].openingWins++;
            }
        }

        static void Draw(GameManager game,PlayerSide side,Result metrics)
        {
            game.TryDrawCard(side);
        }

        static List<Move> Legal(GameManager game,bool limited)
        {
            var board=game.Board.GetBoard(game.State.CurrentPlayer);
            var enemy=game.Board.GetOpponentBoard(game.State.CurrentPlayer);
            var moves=new List<Move>();
            for(int i=0;i<board.hand.Count;i++)
            {
                var card=board.hand[i];if(card.cost>board.currentCost)continue;
                if(card.cardType==CardType.Unit)
                {
                    for(int lane=0;lane<4;lane++)if(board.lanes[lane]==null)
                    {
                        if(!limited && simulateWeaponBattlecry && card.HasKeyword(CardKeyword.Weapon) && enemy.lanes.Any(c=>c!=null))
                        {
                            for(int target=0;target<4;target++)if(enemy.lanes[target]!=null)moves.Add(new Move{hand=i,lane=lane,target=target});
                        }
                        else moves.Add(new Move{hand=i,lane=lane,target=-1});
                    }
                }
                else if(!limited)
                {
                    if(card.HasKeyword(CardKeyword.Remove))
                    {
                        if(game.Rules.globalClear)
                        {
                            if(enemy.lanes.Any(c=>c!=null)||board.lanes.Any(c=>c!=null))moves.Add(new Move{hand=i,lane=-1,target=-1});
                        }
                        else for(int target=0;target<4;target++)if(enemy.lanes[target]!=null)moves.Add(new Move{hand=i,lane=-1,target=target});
                    }
                    else if(card.HasKeyword(CardKeyword.Draw)?board.deck.Count>0:
                        card.HasKeyword(CardKeyword.Heal)?board.heroDamage>0:
                        card.HasKeyword(CardKeyword.Cleave)?enemy.lanes.Any(c=>c!=null):
                        card.HasKeyword(CardKeyword.Buff)&&board.lanes.Any(c=>c!=null))
                        moves.Add(new Move{hand=i,lane=-1,target=-1});
                }
            }
            return moves;
        }

        static void Apply(GameManager game,Move move,bool limited)
        {
            var side=game.State.CurrentPlayer;var board=game.Board.GetBoard(side);
            var card=board.hand[move.hand];
            if(card.cardType==CardType.Spell)
            {
                if(!game.TryCastSpell(card,side,move.target))throw new Exception("Legal spell rejected");
                return;
            }
            board.hand.RemoveAt(move.hand);board.currentCost-=card.cost;
            if(card.cardType==CardType.Unit)
            {
                board.TryPlaceOnLane(new CardInstance(card,side),move.lane);
                if(!limited && card.HasKeyword(CardKeyword.Draw))game.DrawCards(side,1);
                if(card.HasKeyword(CardKeyword.Heal))game.ApplyHealToLeftAlly(side,move.lane,card.attack);
                if(card.HasKeyword(CardKeyword.FrontDamage))game.ApplyFrontDamage(side,move.lane,EffectDamage(card.attack, frontDamageCap));
                if(!limited && simulateWeaponBattlecry && card.HasKeyword(CardKeyword.Weapon) && move.target>=0)
                    game.EquipAndSwingWeapon(side,EffectDamage(card.attack, weaponDamageCap),side==PlayerSide.Me?PlayerSide.Opponent:PlayerSide.Me,move.target);
            }
        }

        static double Forecast(GameManager game,PlayerSide side,int policy)
        {
            if(game.IsGameOver)return Evaluate(game,side,policy);
            var copy=Clone(game);copy.EndTurn();
            double now=Evaluate(copy,side,policy);
            if(copy.IsGameOver)return now;
            copy.EndTurn();
            return now*0.6+Evaluate(copy,side,policy)*0.4;
        }

        static bool ShouldCoin(GameManager game,int policy,bool limited)
        {
            var side=game.State.CurrentPlayer;
            int mana=game.Board.GetBoard(side).currentCost;
            if(mana>=GameRules.MaxCost+(game.Rules.coinExceedsCap?1:0))return false;
            double best=Forecast(game,side,policy);
            foreach(var move in Legal(game,limited))
            {
                var copy=Clone(game);Apply(copy,move,limited);
                best=Math.Max(best,Forecast(copy,side,policy));
                if(!copy.IsGameOver)
                foreach(var next in Legal(copy,limited))
                {
                    var two=Clone(copy);Apply(two,next,limited);
                    best=Math.Max(best,Forecast(two,side,policy));
                }
            }
            var extra=Clone(game);extra.Board.GetBoard(side).currentCost++;
            foreach(var move in Legal(extra,limited))
            {
                var copy=Clone(extra);Apply(copy,move,limited);
                if(copy.Board.GetBoard(side).currentCost==0 && Forecast(copy,side,policy)>best+0.6)return true;
                if(copy.IsGameOver)continue;
                foreach(var next in Legal(copy,limited))
                {
                    var two=Clone(copy);Apply(two,next,limited);
                    if(two.Board.GetBoard(side).currentCost==0 && Forecast(two,side,policy)>best+0.6)return true;
                }
            }
            return false;
        }

        static double Evaluate(GameManager game,PlayerSide side,int policy)
        {
            var me=game.Board.GetBoard(side);var enemy=game.Board.GetOpponentBoard(side);
            if(me.IsDefeated)return -10000;if(enemy.IsDefeated)return 10000;
            return Value(me,policy,game.Rules.mutualMinionDamage)-Value(enemy,policy,game.Rules.mutualMinionDamage);
        }

        static double Value(PlayerBoardState board,int policy,bool mutualMinionDamage)
        {
            double health=policy==1?0.45:0.28,attack=policy==2?0.3:0.17,face=policy==2?4:3;
            return -board.heroDamage*face*10.0/board.maxHeroHealth+board.hand.Count*0.85+board.deck.Count*0.04+
                board.lanes.Where(c=>c!=null&&c.IsAlive).Sum(c=>2.7+c.currentHealth*health+c.currentAttack*attack+(!mutualMinionDamage&&c.data.HasKeyword(CardKeyword.Counter)?0.7:0));
        }

        static GameManager Clone(GameManager source)
        {
            var copy=new GameManager(rules:source.Rules);copy.StartGame(source.State.CurrentPlayer,dealCards:false);
            CopyBoard(source.Board.me,copy.Board.me);CopyBoard(source.Board.opponent,copy.Board.opponent);
            TurnField.SetValue(copy,source.TurnNumber);return copy;
        }

        static void CopyBoard(PlayerBoardState src,PlayerBoardState dst)
        {
            dst.isSecondPlayer=src.isSecondPlayer;dst.ownTurnCount=src.ownTurnCount;
            dst.currentCost=src.currentCost;dst.maxCost=src.maxCost;dst.faceHitCount=src.faceHitCount;
            dst.maxHeroHealth=src.maxHeroHealth;
            dst.fatigueDamage=src.fatigueDamage;
            dst.hand.AddRange(src.hand);dst.deck.AddRange(src.deck);
            for(int i=0;i<4;i++)if(src.lanes[i]!=null)
            {
                var c=src.lanes[i];dst.lanes[i]=new CardInstance(c.data,c.owner){currentAttack=c.currentAttack,currentHealth=c.currentHealth,tempAttackBonus=c.tempAttackBonus,hasAttackedThisTurn=c.hasAttackedThisTurn};
            }
        }

        static void Shuffle<T>(List<T> list,System.Random rng)
        {
            for(int i=list.Count-1;i>0;i--){int j=rng.Next(i+1);var temp=list[i];list[i]=list[j];list[j]=temp;}
        }
    }
}

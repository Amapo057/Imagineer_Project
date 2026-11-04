using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TwoHeadsetTestVerification
{
    static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
    static void Call(GameNetworkManager net, string method) => typeof(GameNetworkManager).GetMethod(method, Private).Invoke(net, null);
    sealed class Remote
    {
        public GameNetworkManager net;
        public GameManager game;
        public KJH.Balancing.SecondPlayerCoin coin;
        public int starts;
        public int[] pending;
        public readonly HashSet<int> used = new HashSet<int>(), spells = new HashSet<int>();
        CardDatabase database;
        public void Initialize(CardDatabase cards)
        {
            database = cards; net.ConfigureMatch(cards, "TWO_HEADSET_DIAGNOSTIC");
            net.OnGameStartReceived += side => { Reset(side); starts++; };
            net.OnRematchReceived += () => Reset(PlayerSide.Me);
            net.OnEndTurnReceived += () => game.EndTurn();
            net.OnCoinUsedReceived += () => coin.TryUse(PlayerSide.Opponent);
            net.OnDrawConfirmedReceived += () => pending[1]--;
            net.OnCardPlayedReceived += (id, lane) => Place(id, lane, PlayerSide.Opponent);
            net.OnSpellCastReceived += (id, lane) =>
            {
                if (spells.Add(id)) Check(game.TryCastSpell(database.GetCardById(id), PlayerSide.Opponent, lane, consumeHand: false), "Remote spell failed");
            };
        }
        void Reset(PlayerSide first)
        {
            pending = new int[2]; used.Clear(); spells.Clear();
            game = new GameManager(false, new MatchRuleSet { fatigueOnEmptyDraw = false });
            game.OnDrawRequested += (side, count) => pending[(int)side] += count;
            game.StartGame(first); coin = new KJH.Balancing.SecondPlayerCoin(game);
        }
        public void Place(int id, int lane, PlayerSide side)
        {
            var card = database.GetCardById(id); var board = game.Board.GetBoard(side);
            Check(game.State.CurrentPlayer == side && board.currentCost >= card.cost && board.lanes[lane] == null, "Invalid remote placement");
            Check(used.Add((int)side * 100 + id), "Remote card reused");
            board.currentCost -= card.cost; board.lanes[lane] = new CardInstance(card, side);
            if (card.HasKeyword(CardKeyword.Draw)) game.DrawCards(side, 1);
        }
        public void Play()
        {
            foreach (int id in new[] { 1, 3, 5 })
                if (!used.Contains(id) && game.Board.me.currentCost >= database.GetCardById(id).cost)
                    for (int lane = 0; lane < 4; lane++)
                        if (game.Board.me.lanes[lane] == null) { Place(id, lane, PlayerSide.Me); net.SendCardPlayed(id, lane); return; }
        }
        public void EndTurn() { Check(net.TrySendEndTurn(PlayerSide.Me, game.State.CurrentPlayer), "Remote end rejected"); game.EndTurn(); }
    }
    static bool Equal(TwoHeadsetTestSession local, Remote remote)
    {
        if (!local.Ready || remote.game == null || local.Game.TurnNumber != remote.game.TurnNumber || local.Game.State.CurrentPlayer == remote.game.State.CurrentPlayer) return false;
        for (int side = 0; side < 2; side++)
        {
            var a = local.Game.Board.GetBoard((PlayerSide)side); var b = remote.game.Board.GetBoard((PlayerSide)(1 - side));
            if (a.heroDamage != b.heroDamage || a.currentCost != b.currentCost || a.maxCost != b.maxCost || a.ownTurnCount != b.ownTurnCount ||
                VRGameSession.PendingPhysicalDraws((PlayerSide)side) != remote.pending[1 - side] ||
                VRGameSession.Coin.IsAvailable((PlayerSide)side) != remote.coin.IsAvailable((PlayerSide)(1 - side))) return false;
            for (int lane = 0; lane < 4; lane++)
            {
                var x = a.lanes[lane]; var y = b.lanes[lane];
                if ((x == null) != (y == null)) return false;
                if (x != null && (x.data.cardId != y.data.cardId || x.currentAttack != y.currentAttack || x.currentHealth != y.currentHealth)) return false;
            }
        }
        return true;
    }
    static void Wait(TwoHeadsetTestSession local, Remote remote, Func<bool> done, string text)
    {
        var end = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < end)
        {
            Call(local.Network, "Update"); Call(remote.net, "Update");
            if (done()) return;
            Thread.Sleep(5);
        }
        throw new Exception(text);
    }
    public static void Run()
    {
        NetworkRecoveryVerification.Run();
        EditorSceneManager.OpenScene(TwoHeadsetTestBuilder.ScenePath);
        var local = UnityEngine.Object.FindFirstObjectByType<TwoHeadsetTestSession>();
        var ui = local.GetComponent<TwoHeadsetTestUI>();
        typeof(GameNetworkManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        Call(local.GetComponent<GameNetworkManager>(), "Awake");
        local.Initialize(); ui.BuildNow();
        Remote remote = null;
        try
        {
            Check(ui.Buttons.ContainsKey("ConnectIP") && ui.Buttons.ContainsKey("Rematch"), "Missing UI actions");
            Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length == 1, "Need exactly one EventSystem");
            typeof(GameNetworkManager).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            remote = new Remote { net = new GameObject("UI verification other headset").AddComponent<GameNetworkManager>() };
            Call(remote.net, "Awake"); remote.Initialize(local.Database); remote.net.StartManualHost();
            foreach (char c in "127.0.0.1") ui.Buttons[c == '.' ? "KeyDot" : "Key" + c].onClick.Invoke();
            ui.Buttons["ConnectIP"].onClick.Invoke();
            Wait(local, remote, () => Equal(local, remote), "Keypad/IP UI did not connect and synchronize");
            Check(local.Network.Mode == GameNetworkManager.ConnectionMode.Client, "Client UI chose wrong connection mode");
            Debug.Log("TWO_HEADSET_IP_UI_PASS");

            for (int turn = 0; turn < 4; turn++)
            {
                if (local.CanAct)
                {
                    if (local.Game.Board.me.isSecondPlayer && local.Game.Board.me.ownTurnCount == 1) ui.Buttons["Coin"].onClick.Invoke();
                    Wait(local, remote, () => Equal(local, remote), "Coin state mismatch");
                    ui.Buttons["PlaceCard"].onClick.Invoke();
                }
                else
                {
                    int before = local.Game.TurnNumber; ui.Buttons["EndTurn"].onClick.Invoke();
                    Check(local.Game.TurnNumber == before, "UI allowed opponent-turn action");
                    if (remote.game.Board.me.isSecondPlayer && remote.game.Board.me.ownTurnCount == 1)
                    { Check(remote.coin.TryUse(PlayerSide.Me), "Remote coin failed"); remote.net.SendCoinUsed(); }
                    remote.Play();
                }
                Wait(local, remote, () => Equal(local, remote), "Card state mismatch");
                if (local.CanAct) ui.Buttons["EndTurn"].onClick.Invoke(); else remote.EndTurn();
                Wait(local, remote, () => Equal(local, remote), "Turn state mismatch");
            }
            Debug.Log("TWO_HEADSET_CARD_TURN_COIN_UI_PASS");
            foreach (int spell in new[] { 27, 26, 25, 24 })
            {
                if (!local.CanAct) { remote.EndTurn(); Wait(local, remote, () => Equal(local, remote) && local.CanAct, "Return own turn failed"); }
                int beforeCost = local.Game.Board.me.currentCost;
                ui.Buttons["Spell" + spell].onClick.Invoke();
                Wait(local, remote, () => Equal(local, remote), "Spell state mismatch " + spell);
                Check(local.Game.Board.me.currentCost == beforeCost - 1, "Spell UI did not consume one cost " + spell);
                ui.Buttons["EndTurn"].onClick.Invoke(); Wait(local, remote, () => Equal(local, remote), "Spell turn end mismatch");
            }
            ui.Buttons["DrawConfirm"].onClick.Invoke(); Wait(local, remote, () => Equal(local, remote), "Draw confirmation mismatch");
            Debug.Log("TWO_HEADSET_SPELL_DRAW_UI_PASS");
            int starts = local.Starts;
            ui.Buttons["Rematch"].onClick.Invoke();
            Wait(local, remote, () => local.Starts > starts && Equal(local, remote), "UI rematch failed");
            Check(local.Game.Board.me.HeroHealth == 20 && local.Game.Board.opponent.HeroHealth == 20, "Rematch HP not reset");
            ui.Refresh(); Check(local.StateCode().Length == 8, "Canonical state code missing");
            Debug.Log("TWO_HEADSET_UI_VERIFICATION_SUCCESS");
        }
        finally
        {
            if (remote?.net != null) UnityEngine.Object.DestroyImmediate(remote.net.gameObject);
            if (local != null) UnityEngine.Object.DestroyImmediate(local.gameObject);
        }
    }
}

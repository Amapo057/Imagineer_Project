using System;
using System.Linq;
using System.Reflection;
using KJH.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProductionGamePlayVerification
{
    const string Key = "KJH.ProductionGamePlayVerification";
    static double start;
    static int frame;
    static CharacterMotion left, right;
    static bool sawLeft, sawRight;
    static ProductionGamePlayVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            start = EditorApplication.timeSinceStartup; frame = Time.frameCount;
            EditorApplication.update += Check;
        };
    }
    public static void Start()
    {
        ProductionGameBuilder.Generate();
        foreach (var cameraAccess in UnityEngine.Object.FindObjectsByType<Meta.XR.PassthroughCameraAccess>(FindObjectsSortMode.None)) cameraAccess.enabled = false; // Simulate unavailable camera; never saved.
        var xr = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        if (xr != null) xr.InitManagerOnStart = false; // PC fixture only, never saved.
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    static void Check()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup - start < 3) return;
        try
        {
            var flow = UnityEngine.Object.FindFirstObjectByType<ProductionGameController>();
            var ui = UnityEngine.Object.FindFirstObjectByType<ProductionGameUI>();
            if (Time.frameCount <= frame + 5 || ui == null || !ui.Built || flow == null || flow.CanAct)
                throw new Exception("Production Play startup/UI/input gate verification failed");
            if (flow.detector.CameraReady) throw new Exception("Camera should wait for real hardware in the PC fixture");
            Debug.Log("PRODUCTION_PLAY_STARTUP_PASS: actual MR scene frames advance, production UI builds, absent camera waits safely, gameplay waits for peer/hardware");
            ProductionPolishVerification.Capture(ui, flow);
            EditorApplication.update -= Check;
            StartCombatCheck(flow);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void StartCombatCheck(ProductionGameController flow)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        flow.detector = null; flow.anchor = null; flow.seating = null;
        typeof(GameNetworkManager).GetField("connected", Private).SetValue(flow.Network, true);
        var game = flow.Game;
        game.StartGame(PlayerSide.Me, dealCards: false);
        typeof(ProductionGameController).GetMethod("OnStarted", Private).Invoke(flow, new object[] { PlayerSide.Me });
        foreach (var board in new[] { game.Board.me, game.Board.opponent }) Array.Clear(board.lanes, 0, board.lanes.Length);
        game.EndTurn();
        game.Board.me.heroDamage = game.Board.me.maxHeroHealth - 1;
        game.Board.me.lanes[0] = new CardInstance(flow.Database.GetCardById(9), PlayerSide.Me) { currentHealth = 1 };
        game.Board.opponent.lanes[0] = new CardInstance(flow.Database.GetCardById(5), PlayerSide.Opponent);
        game.Board.opponent.lanes[2] = new CardInstance(flow.Database.GetCardById(14), PlayerSide.Opponent);
        game.Board.opponent.lanes[3] = new CardInstance(flow.Database.GetCardById(20), PlayerSide.Opponent);
        start = EditorApplication.timeSinceStartup;
        EditorApplication.update += ObserveCombat;
    }
    static void ObserveCombat()
    {
        try
        {
            var flow = ProductionGameController.Current;
            double elapsed = EditorApplication.timeSinceStartup - start;
            if (elapsed > 15) throw new Exception("Sequential combat did not finish");
            if (left == null || right == null)
            {
                var slots = UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None);
                left = slots.Single(slot => slot.lane == 0).GetComponentInChildren<CharacterMotion>();
                right = slots.Single(slot => slot.lane == 2).GetComponentInChildren<CharacterMotion>();
                if (left == null || right == null) return;
                const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameNetworkManager).GetMethod("HandleLine", Private).Invoke(flow.Network, new object[] { "END_TURN" });
                typeof(GameNetworkManager).GetMethod("Update", Private).Invoke(flow.Network, null);
                var feedback = flow.GetComponent<ProductionTableFeedback>();
                int displayedHealth = (int)typeof(ProductionTableFeedback).GetMethod("DisplayedHeroHealth", Private)
                    .Invoke(feedback, new object[] { PlayerSide.Me });
                if (!flow.CombatPlaying || !flow.Game.IsGameOver || displayedHealth != 1)
                    throw new Exception("Combat must capture the pre-hit view while resolving the authoritative board");
                return;
            }
            sawLeft |= left.IsAttackAnimating;
            if (right.IsAttackAnimating)
            {
                if (!sawLeft || left.IsAttackAnimating)
                    throw new Exception("Right lane started before the left attack finished");
                sawRight = true;
            }
            if (flow.CombatPlaying) return;
            var last = UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None)
                .Single(slot => slot.lane == 3).GetComponentInChildren<CharacterMotion>();
            if (!sawLeft || !sawRight || flow.Game.Board.me.HeroHealth != 0 || last.IsAttackAnimating || last.IsPlaying)
                throw new Exception("Left-to-right combat, skipped lane or lethal stop failed");
            Debug.Log("PRODUCTION_SEQUENTIAL_COMBAT_PASS: actual attack states in order, empty lane skipped, hero health delayed, lethal stops remaining lanes");
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Check; EditorApplication.update -= ObserveCombat;
        EditorApplication.Exit(code);
    }
}



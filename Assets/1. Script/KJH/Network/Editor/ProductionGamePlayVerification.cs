using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class ProductionGamePlayVerification
{
    const string Key = "KJH.ProductionGamePlayVerification";
    static double start;
    static int frame;
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
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Finish(int code)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Check; EditorApplication.Exit(code);
    }
}



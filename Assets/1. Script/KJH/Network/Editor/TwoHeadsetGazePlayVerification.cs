using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TwoHeadsetGazePlayVerification
{
    const string Key = "KJH.TwoHeadsetGazePlayVerification";
    static double started;
    static int initialFrame, stage;
    static RenderTexture target;
    static TwoHeadsetTestUI ui;
    static string pendingPreview;
    static TwoHeadsetGazePlayVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
            started = EditorApplication.timeSinceStartup; initialFrame = Time.frameCount; stage = 0;
            EditorApplication.update += Tick;
            UnityEngine.Rendering.RenderPipelineManager.endFrameRendering += CaptureCompletedFrame;
        };
    }
    public static void Start()
    {
        EditorSceneManager.OpenScene(TwoHeadsetTestBuilder.ScenePath);
        var xr = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        if (xr != null)
        {
            xr.InitManagerOnStart = false;
            if (xr.Manager != null) { xr.Manager.automaticLoading = false; xr.Manager.automaticRunning = false; }
        }
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, name);
        if (at < 0 || at + 1 >= args.Length) throw new Exception("Missing " + name);
        return args[at + 1];
    }
    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) return;
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (elapsed > 40) throw new Exception("Gaze play test timeout");
            if (stage == 0 && elapsed > 2)
            {
                Check(Time.frameCount > initialFrame + 5, "Play frames are frozen");
                ui = UnityEngine.Object.FindFirstObjectByType<TwoHeadsetTestUI>();
                Check(ui != null && ui.Built, "UI did not initialize in Play mode");
                Check(ui.GetComponent<TwoHeadsetTestSession>().Database.allCards.Count == 27, "Snapshot did not load");
                target = new RenderTexture(1536, 1536, 24); target.Create(); ui.viewCamera.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                var button = ui.Buttons["Host"].GetComponent<RectTransform>();
                ui.viewCamera.transform.LookAt(button.TransformPoint(button.rect.center));
                stage = 1; started = EditorApplication.timeSinceStartup;
            }
            else if (stage == 1 && elapsed > 3)
            {
                Check(ui.GetComponent<TwoHeadsetTestSession>().Network.Mode == GameNetworkManager.ConnectionMode.Host,
                    "Actual gaze dwell did not activate the host button");
                ui.viewCamera.transform.rotation = Quaternion.identity;
                stage = 2; started = EditorApplication.timeSinceStartup;
            }
            else if (stage == 2 && elapsed > 0.3)
            {
                DirtyCanvas(); pendingPreview = Arg("-lanPreview"); stage = 20;
            }
            else if (stage == 20 && pendingPreview == null)
            {
                ui.Buttons["GameplayTab"].onClick.Invoke(); stage = 3; started = EditorApplication.timeSinceStartup;
            }
            else if (stage == 3 && elapsed > 0.3)
            {
                DirtyCanvas(); pendingPreview = Path.Combine(Path.GetDirectoryName(Arg("-lanPreview")), "gameplay_preview.png"); stage = 30;
            }
            else if (stage == 30 && pendingPreview == null)
            {
                ui.Buttons["ConnectionTab"].onClick.Invoke(); stage = 4; started = EditorApplication.timeSinceStartup;
            }
            else if (stage == 4 && elapsed > 0.3)
            {
                DirtyCanvas(); pendingPreview = Arg("-lanPreview"); stage = 40;
            }
            else if (stage == 40 && pendingPreview == null)
            {
                string path = Arg("-lanPreview");
                Debug.Log("TWO_HEADSET_GAZE_PLAY_SUCCESS: advancing frames; XR-independent UI; real 1.5-second gaze activates host; preview=" + path);
                SessionState.SetBool(Key, false); EditorApplication.update -= Tick; EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e); SessionState.SetBool(Key, false); EditorApplication.update -= Tick; EditorApplication.Exit(1);
        }
    }
    static void DirtyCanvas()
    {
        foreach (var graphic in ui.GetComponentsInChildren<UnityEngine.UI.Graphic>()) graphic.SetAllDirty();
        Canvas.ForceUpdateCanvases();
    }
    static void CaptureCompletedFrame(UnityEngine.Rendering.ScriptableRenderContext context, Camera[] cameras)
    {
        if (pendingPreview == null || target == null) return;
        SavePreview(pendingPreview); pendingPreview = null;
    }
    static void SavePreview(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); RenderTexture.active = previous;
    }
}

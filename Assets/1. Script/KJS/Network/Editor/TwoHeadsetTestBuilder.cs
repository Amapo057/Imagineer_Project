using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TwoHeadsetTestBuilder
{
    public const string Folder = "Assets/1. Script/KJH/Network/Testing";
    public const string ScenePath = Folder + "/KJH_TwoHeadsetTest.unity";
    public const string SnapshotPath = Folder + "/LanTestCards.json";
    static string Argument(string name, string fallback)
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }

    [MenuItem("Imagineer/LAN Test/1. 테스트 씬 생성")]
    public static void Generate()
    {
        var database = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
        if (database == null || database.allCards.Count(c => c != null) != 27) throw new Exception("원본 카드 데이터베이스 27종을 확인하세요.");
        Directory.CreateDirectory(Folder);
        var snapshot = new TwoHeadsetTestSession.Snapshot
        {
            build = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            cards = database.allCards.Where(c => c != null).OrderBy(c => c.cardId).Select(c => new TwoHeadsetTestSession.Entry
            { id = c.cardId, name = c.cardName, cost = c.cost, attack = c.attack, health = c.health, keywords = (int)c.keywords, type = (int)c.cardType }).ToArray()
        };
        File.WriteAllText(SnapshotPath, JsonUtility.ToJson(snapshot, true)); AssetDatabase.ImportAsset(SnapshotPath);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cameraObject = new GameObject("LAN Test Camera", typeof(Camera), typeof(AudioListener)); cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 1.6f, 0);
        var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.015f, 0.025f, 0.05f); camera.nearClipPlane = 0.05f; camera.farClipPlane = 50;
        camera.stereoTargetEye = StereoTargetEyeMask.Both; camera.fieldOfView = 62;
        var pose = cameraObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        pose.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HMD Position", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        pose.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HMD Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        pose.trackingStateInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("HMD Tracking", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));
        var root = new GameObject("LAN Test", typeof(GameNetworkManager), typeof(TwoHeadsetTestSession), typeof(TwoHeadsetTestUI));
        root.GetComponent<TwoHeadsetTestSession>().cardSnapshot = AssetDatabase.LoadAssetAtPath<TextAsset>(SnapshotPath);
        var ui = root.GetComponent<TwoHeadsetTestUI>(); ui.viewCamera = camera;
        ui.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/NotoSansKR SDF.asset");
        if (ui.font == null) throw new Exception("한글 TMP 폰트 에셋을 확인하세요.");
        EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.SaveAssets();
        Debug.Log("TWO_HEADSET_SCENE_GENERATED: " + ScenePath + "; original game scenes/build list untouched");
    }

    [MenuItem("Imagineer/LAN Test/2. 테스트 APK 빌드")]
    public static void BuildInteractive()
    {
        string path = EditorUtility.SaveFilePanel("두 기기에 똑같이 설치할 테스트 APK", "", "Imagineer_LAN_Test", "apk");
        if (!string.IsNullOrEmpty(path)) BuildTo(path);
    }
    public static void Build() => BuildTo(Argument("-lanOutput", Path.GetFullPath("Builds/Imagineer_LAN_Test.apk")));
    static void BuildTo(string path)
    {
        Generate();
        string product = PlayerSettings.productName, appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android); var architectures = PlayerSettings.Android.targetArchitectures;
        try
        {
            PlayerSettings.productName = "Imagineer LAN Test";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.imagineer.lantest");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            { scenes = new[] { ScenePath }, locationPathName = path, target = BuildTarget.Android, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("테스트 APK 빌드 실패: " + report.summary.result);
            Debug.Log("TWO_HEADSET_APK_BUILT: " + Path.GetFullPath(path));
        }
        finally
        {
            PlayerSettings.productName = product; PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, appId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend); PlayerSettings.Android.targetArchitectures = architectures;
            AssetDatabase.SaveAssets();
        }
    }
}

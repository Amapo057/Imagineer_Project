using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProductionGameBuilder
{
    public const string StartScenePath = "Assets/0. Scene/0 KJS_StartScene.unity";
    public const string ScenePath = "Assets/0. Scene/1 MainGame.unity";
    [MenuItem("Imagineer/실제 게임/1. 적용 씬 확인")]
    public static void Generate()
    {
        if (!File.Exists(ScenePath)) throw new Exception("실제 게임 씬이 없습니다: " + ScenePath);
        var scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsPrefabAssetMissing(child.gameObject))
                    throw new Exception("모델 원본이 누락되었습니다. 팀원에게 모델과 .meta 파일을 받아 주세요: " + child.name);
        var flow = UnityEngine.Object.FindFirstObjectByType<ProductionGameController>();
        if (flow == null || flow.Database == null)
            throw new Exception("실제 게임 세션/카드 데이터가 없습니다.");
        if (UnityEngine.Object.FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None).Length != GameRules.LaneCount ||
            UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None).Length != GameRules.LaneCount)
            throw new Exception("양쪽 필드 슬롯 4개를 확인하세요.");
        Debug.Log("PRODUCTION_GAME_SCENE_READY: " + ScenePath);
    }
    [MenuItem("Imagineer/실제 게임/2. 실제 게임 APK 빌드")]
    public static void BuildInteractive()
    {
        var path = EditorUtility.SaveFilePanel("실제 게임 APK", "", "Imagineer_Game", "apk");
        if (path.Length > 0) BuildTo(path);
    }
    public static void Build()
    {
        var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-gameOutput");
        if (index < 0) throw new Exception("-gameOutput APK 경로가 필요합니다.");
        BuildTo(args[index + 1]);
    }
    static void BuildTo(string path)
    {
        if (!File.Exists(StartScenePath)) throw new Exception("시작 씬이 없습니다: " + StartScenePath);
        Generate();
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
        var architecture = PlayerSettings.Android.targetArchitectures;
        try
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { StartScenePath, ScenePath }, target = BuildTarget.Android, locationPathName = path, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("실제 게임 APK 빌드 실패");
            Debug.Log("PRODUCTION_GAME_APK_BUILT: " + path);
        }
        finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend); PlayerSettings.Android.targetArchitectures = architecture; AssetDatabase.SaveAssets(); }
    }
}




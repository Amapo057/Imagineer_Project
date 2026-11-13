using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProductionGameBuilder
{
    public const string ScenePath = "Assets/1. Script/KJH/Network/Production/MainGame_Play.unity";
    [MenuItem("Imagineer/실제 게임/1. 적용 씬 생성")]
    public static void Generate()
    {
        EditorSceneManager.OpenScene("Assets/0. Scene/MainGame.unity");
        var hud = UnityEngine.Object.FindFirstObjectByType<GameHUDController>();
        var database = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
        if (hud == null || database == null) throw new Exception("실제 HUD/카드 데이터가 없습니다.");
        var oldSlots = UnityEngine.Object.FindObjectsByType<SlotModelSpawner>(FindObjectsSortMode.None);
        if (oldSlots.Length != 4) throw new Exception("실제 필드 슬롯 4개를 확인하세요: " + oldSlots.Length);
        var root = new GameObject("Production Game Flow");
        var flow = root.AddComponent<ProductionGameController>(); flow.Database = database; flow.hud = hud;
        flow.detector = UnityEngine.Object.FindFirstObjectByType<MarkerDetecter>();
        flow.anchor = UnityEngine.Object.FindFirstObjectByType<AnchorManager>();
        var detectorSettings = new SerializedObject(flow.detector);
        detectorSettings.FindProperty("markerDictionary").enumValueIndex = (int)OpenCvSharp.Aruco.PredefinedDictionaryName.Dict4X4_100;
        detectorSettings.FindProperty("waitForCameraReady").boolValue = true;
        detectorSettings.ApplyModifiedPropertiesWithoutUndo();
        var ui = root.AddComponent<ProductionGameUI>();
        ui.viewCamera = Camera.main;
        if (ui.viewCamera == null) throw new Exception("실제 HMD MainCamera가 없습니다.");
        ui.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Fonts/NotoSansKR SDF.asset");
        const string materialPath = "Assets/1. Script/KJH/Network/Production/TableFeedback.mat";
        Directory.CreateDirectory(Path.GetDirectoryName(materialPath));
        ui.tableMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (ui.tableMaterial == null)
        {
            ui.tableMaterial = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(ui.tableMaterial, materialPath);
        }
        foreach (var old in oldSlots)
        {
            var serialized = new SerializedObject(old);
            int lane = serialized.FindProperty("laneIndex").intValue;
            var marker = (MarkerWorldPos)serialized.FindProperty("markerWorldPos").objectReferenceValue;
            if (marker == null) throw new Exception("슬롯 마커 참조 누락: " + old.name);
            float scale = serialized.FindProperty("hologramScale").floatValue;
            old.enabled = false;
            var slot = old.gameObject.AddComponent<ProductionCardSlot>();
            slot.session = flow; slot.marker = marker; slot.lane = lane; slot.modelScale = scale;
            var bridge = old.GetComponent<KJH.Characters.CharacterSlotAnimationBridge>() ?? old.gameObject.AddComponent<KJH.Characters.CharacterSlotAnimationBridge>();
            bridge.side = PlayerSide.Me; bridge.laneIndex = lane;
            var other = new GameObject("Opponent Lane " + lane); other.transform.SetParent(old.transform.parent, false);
            var markerSettings = new SerializedObject(marker);
            var fieldRoot = (GameObject)markerSettings.FindProperty("fieldRoot").objectReferenceValue;
            var local = fieldRoot.transform.InverseTransformPoint(old.transform.position);
            Vector3 opponentPosition = fieldRoot.transform.TransformPoint(new Vector3(local.x, local.y, -local.z));
            if (Vector3.Distance(opponentPosition, old.transform.position) < 0.1f) throw new Exception("상대 필드 위치를 확인하세요.");
            other.transform.SetPositionAndRotation(opponentPosition, old.transform.rotation * Quaternion.Euler(0, 180, 0));
            var view = other.AddComponent<ProductionOpponentView>(); view.lane = lane; view.modelScale = scale;
            var animation = other.AddComponent<KJH.Characters.CharacterSlotAnimationBridge>(); animation.side = PlayerSide.Opponent; animation.laneIndex = lane;
            Debug.Log($"PRODUCTION_LANE {lane}: local={old.transform.position}; opponent={other.transform.position}");
            UnityEngine.Object.DestroyImmediate(old);
        }
        var seat = root.AddComponent<ProductionSeating>(); flow.seating = seat;
        root.AddComponent<ProductionWeaponInput>();
        seat.anchor = flow.anchor; seat.head = ui.viewCamera;
        seat.marker = UnityEngine.Object.FindFirstObjectByType<MarkerWorldPos>();
        seat.field = ((GameObject)new SerializedObject(seat.marker).FindProperty("fieldRoot").objectReferenceValue).transform;
        seat.localObjects = UnityEngine.Object.FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None).Select(x => x.transform)
            .Concat(UnityEngine.Object.FindObjectsByType<DeckSlot>(FindObjectsSortMode.None).Select(x => x.transform))
            .Concat(UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None).Where(x => x.name == "End Turn").Select(x => x.transform)).Distinct().ToArray();
        seat.remoteObjects = UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None).Select(x => x.transform).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("PRODUCTION_GAME_SCENE_READY: original MR camera, marker tracking, hands, table and HUD retained; " + ScenePath);
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
        Generate();
        var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);
        var architecture = PlayerSettings.Android.targetArchitectures;
        try
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, target = BuildTarget.Android, locationPathName = path, options = BuildOptions.Development });
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("실제 게임 APK 빌드 실패");
            Debug.Log("PRODUCTION_GAME_APK_BUILT: " + path);
        }
        finally { PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, backend); PlayerSettings.Android.targetArchitectures = architecture; AssetDatabase.SaveAssets(); }
    }
}




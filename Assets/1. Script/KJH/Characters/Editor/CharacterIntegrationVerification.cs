using System;
using System.IO;
using System.Linq;
using KJH.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class CharacterIntegrationVerification
{
    const string Root = "Assets/1. Script/KJH/Characters";
    const string RunKey = "KJH.CharacterValidation";
    static CharacterMotion[] motions;
    static bool[] attacks, hits, returned;
    static double started;
    static int stage;
    static bool sawBridgeAttack, sawBridgeHit;
    static CharacterMotion own, opponent;

    static CharacterIntegrationVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
            {
                started = EditorApplication.timeSinceStartup;
                stage = 0;
                EditorApplication.update += Tick;
            }
        };
    }

    public static void StartRuntimeValidation()
    {
        EditorSceneManager.OpenScene(Root + "/Scenes/CharacterPreview.unity");
        SessionState.SetBool(RunKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) return;
            double elapsed = EditorApplication.timeSinceStartup - started;
            if (elapsed > 90) throw new Exception("Runtime validation timeout at stage " + stage);
            if (stage == 0 && elapsed > 0.5)
            {
                motions = UnityEngine.Object.FindObjectsByType<CharacterMotion>(FindObjectsSortMode.None);
                if (motions.Length != 10) throw new Exception("Expected ten preview characters");
                attacks = new bool[10]; hits = new bool[10]; returned = new bool[10];
                foreach (var motion in motions) { motion.Attack(); motion.Hit(); }
                stage = 1;
            }
            if (stage == 1)
            {
                for (int i = 0; i < motions.Length; i++)
                {
                    var state = motions[i].animator.GetCurrentAnimatorStateInfo(0);
                    attacks[i] |= state.IsName("Attack");
                    hits[i] |= state.IsName("Hit");
                    returned[i] |= hits[i] && state.IsName("Idle");
                }
                if (returned.All(x => x))
                {
                    if (!attacks.All(x => x) || !hits.All(x => x)) throw new Exception("Motion queue skipped a state");
                    Debug.Log("CHARACTER_RUNTIME_QUEUE_SUCCESS: all 10 Attack -> Hit -> Idle");
                    own = MakeSlot(PlayerSide.Me);
                    opponent = MakeSlot(PlayerSide.Opponent);
                    PrepareCombat();
                    stage = 2;
                }
            }
            else if (stage == 2 || stage == 4)
            {
                sawBridgeAttack |= opponent.animator.GetCurrentAnimatorStateInfo(0).IsName("Attack");
                sawBridgeHit |= own.animator.GetCurrentAnimatorStateInfo(0).IsName("Hit");
                if (sawBridgeAttack && sawBridgeHit)
                {
                    if (stage == 4) { Finish(); return; }
                    Debug.Log("CHARACTER_RUNTIME_BRIDGE_SUCCESS: combat events reached matching side/lane");
                    VRGameSession.ResetGame();
                    sawBridgeAttack = sawBridgeHit = false;
                    own.animator.Play("Idle"); opponent.animator.Play("Idle");
                    stage = 3;
                    started = EditorApplication.timeSinceStartup;
                }
            }
            else if (stage == 3 && elapsed > 1)
            {
                PrepareCombat();
                stage = 4;
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.SetBool(RunKey, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(1);
        }
    }

    static CharacterMotion MakeSlot(PlayerSide side)
    {
        var slot = new GameObject("Validation slot " + side);
        var bridge = slot.AddComponent<CharacterSlotAnimationBridge>();
        bridge.side = side;
        bridge.laneIndex = 0;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/Garen.prefab");
        return UnityEngine.Object.Instantiate(prefab, slot.transform).GetComponent<CharacterMotion>();
    }

    static void PrepareCombat()
    {
        var data = ScriptableObject.CreateInstance<CardData>();
        data.attack = 1; data.health = 999; data.keywords = CardKeyword.Counter;
        var game = VRGameSession.Game;
        game.Board.GetBoard(PlayerSide.Me).lanes[0] = new CardInstance(data, PlayerSide.Me);
        game.Board.GetBoard(PlayerSide.Opponent).lanes[0] = new CardInstance(data, PlayerSide.Opponent);
        game.EndTurn();
        game.EndTurn();
    }

    static void Finish()
    {
        Debug.Log("CHARACTER_RUNTIME_REMATCH_SUCCESS: bridge reconnected after ResetGame");
        Debug.Log("CHARACTER_RUNTIME_VALIDATION_SUCCESS");
        SessionState.SetBool(RunKey, false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    public static void Capture()
    {
        EditorSceneManager.OpenScene(Root + "/Scenes/CharacterPreview.unity");
        foreach (var old in UnityEngine.Object.FindObjectsByType<CharacterMotion>(FindObjectsSortMode.None))
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            instance.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        }
        foreach (var motion in UnityEngine.Object.FindObjectsByType<CharacterMotion>(FindObjectsSortMode.None))
        {
            motion.animator.Rebind();
            motion.animator.Play("Idle", 0, 0.25f);
            motion.animator.Update(0);
        }
        var args = Environment.GetCommandLineArgs();
        var output = args[Array.IndexOf(args, "-characterPreview") + 1];
        var camera = Camera.main;
        var target = new RenderTexture(1800, 1000, 24);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1800, 1000, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1800, 1000), 0, 0);
        image.Apply();
        File.WriteAllBytes(output, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("CHARACTER_PREVIEW_SAVED: " + output);
    }

    public static void CapturePetDirections()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        string[] names = { "NaelleumMeong", "MasitNyang" };
        for (int row = 0; row < 2; row++)
        for (int column = 0; column < 4; column++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + names[row] + ".prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3((column - 1.5f) * 2.4f, 0, row * 3.2f);
            instance.transform.Find("Facing").localRotation = Quaternion.Euler(0, column * 90, 0);
            var animator = instance.GetComponentInChildren<Animator>();
            animator.Rebind(); animator.Play("Idle", 0, 0.25f); animator.Update(0);
        }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 6, -10);
        camera.transform.LookAt(new Vector3(0, 0.7f, 1.5f));
        camera.orthographic = true; camera.orthographicSize = 3.6f;
        camera.backgroundColor = new Color(0.09f, 0.11f, 0.14f); camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(1600, 900, 24); camera.targetTexture = target; camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); image.Apply();
        var args = Environment.GetCommandLineArgs();
        File.WriteAllBytes(args[Array.IndexOf(args, "-characterPreview") + 1], image.EncodeToPNG());
    }
}

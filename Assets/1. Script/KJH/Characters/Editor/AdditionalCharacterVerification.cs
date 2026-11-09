using System;
using System.Linq;
using KJH.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AdditionalCharacterVerification
{
    const string RunKey = "KJH.AdditionalCharacterValidation";
    static CharacterMotion[] motions, combat;
    static CharacterMotion decoy;
    static bool[] spawned, attacks, hits;
    static double started, stepStarted;
    static int stage;

    static AdditionalCharacterVerification()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RunKey, false)) return;
            started = stepStarted = EditorApplication.timeSinceStartup;
            stage = 0;
            motions = UnityEngine.Object.FindObjectsByType<CharacterMotion>(FindObjectsSortMode.None);
            spawned = new bool[motions.Length];
            EditorApplication.update += Tick;
        };
    }

    public static void StartRuntimeValidation()
    {
        EditorSceneManager.OpenScene(AdditionalCharacterBuilder.Root + "/Scenes/AdditionalCharacterPreview.unity");
        SessionState.SetBool(RunKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 75) throw new Exception("Additional character validation timeout at stage " + stage);
            if (motions.Length != 3) throw new Exception("Expected three new characters");
            if (stage == 0)
            {
                for (int i = 0; i < motions.Length; i++) spawned[i] |= motions[i].animator.GetCurrentAnimatorStateInfo(0).IsName("Spawn");
                if (now - stepStarted > .2 && motions.All(m => m.animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")))
                {
                    if (!spawned.All(x => x)) throw new Exception("Spawn did not play for every new character");
                    attacks = new bool[3]; hits = new bool[3];
                    foreach (var motion in motions) { motion.Attack(); motion.Hit(); }
                    stage = 1;
                }
            }
            else if (stage == 1)
            {
                Observe(motions);
                if (hits.All(x => x) && motions.All(m => m.animator.GetCurrentAnimatorStateInfo(0).IsName("Idle")))
                {
                    if (!attacks.All(x => x)) throw new Exception("Attack queue skipped a state");
                    Debug.Log("ADDITIONAL_CHARACTER_RUNTIME_QUEUE_PASS: all three Spawn -> Idle; Attack -> Hit -> Idle");
                    var prefabs = motions.Select(m => AssetDatabase.LoadAssetAtPath<GameObject>(AdditionalCharacterBuilder.Root + "/Prefabs/" + m.name.Replace("(Clone)", "") + ".prefab")).ToArray();
                    combat = new CharacterMotion[6];
                    for (int i = 0; i < 6; i++) combat[i] = Slot(prefabs[i % 3], i < 3 ? PlayerSide.Me : PlayerSide.Opponent, i % 3);
                    decoy = Slot(prefabs[0], PlayerSide.Me, 3);
                    stepStarted = now; stage = 2;
                }
            }
            else if ((stage == 2 || stage == 4) && now - stepStarted > 1)
            {
                foreach (var m in combat) m.animator.Play("Idle");
                decoy.animator.Play("Idle");
                attacks = new bool[6]; hits = new bool[6];
                PrepareCombat(); stage++;
            }
            else if (stage == 3 || stage == 5)
            {
                Observe(combat);
                var unused = decoy.animator.GetCurrentAnimatorStateInfo(0);
                if (unused.IsName("Attack") || unused.IsName("Hit")) throw new Exception("Empty adjacent lane received combat animation");
                if (attacks.All(x => x) && hits.All(x => x))
                {
                    if (stage == 5) { Finish(0); return; }
                    Debug.Log("ADDITIONAL_CHARACTER_RUNTIME_BRIDGE_PASS: all three on both sides; empty adjacent lane excluded");
                    VRGameSession.ResetGame(); stage = 4; stepStarted = now;
                }
            }
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }

    static void Observe(CharacterMotion[] list)
    {
        for (int i = 0; i < list.Length; i++)
        {
            var state = list[i].animator.GetCurrentAnimatorStateInfo(0);
            attacks[i] |= state.IsName("Attack"); hits[i] |= state.IsName("Hit");
        }
    }
    static CharacterMotion Slot(GameObject prefab, PlayerSide side, int lane)
    {
        var slot = new GameObject("Validation " + side + " lane " + lane);
        slot.SetActive(false);
        var bridge = slot.AddComponent<CharacterSlotAnimationBridge>(); bridge.side = side; bridge.laneIndex = lane;
        var motion = UnityEngine.Object.Instantiate(prefab, slot.transform).GetComponent<CharacterMotion>();
        slot.SetActive(true);
        return motion;
    }
    static void PrepareCombat()
    {
        var data = ScriptableObject.CreateInstance<CardData>();
        data.attack = 1; data.health = 999; data.keywords = CardKeyword.Counter;
        var game = VRGameSession.Game;
        for (int lane = 0; lane < 3; lane++)
        {
            game.Board.GetBoard(PlayerSide.Me).lanes[lane] = new CardInstance(data, PlayerSide.Me);
            game.Board.GetBoard(PlayerSide.Opponent).lanes[lane] = new CardInstance(data, PlayerSide.Opponent);
        }
        game.EndTurn(); game.EndTurn(); game.EndTurn();
    }
    static void Finish(int code)
    {
        if (code == 0) Debug.Log("ADDITIONAL_CHARACTER_RUNTIME_SUCCESS: queue, both sides, lane isolation and rematch rebind");
        SessionState.SetBool(RunKey, false); EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}

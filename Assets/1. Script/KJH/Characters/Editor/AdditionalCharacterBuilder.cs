using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KJH.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AdditionalCharacterBuilder
{
    public const string Root = "Assets/1. Script/KJH/Characters/Additional20261109";
    [Serializable] public class MaterialInfo { public string name, texture; public bool transparent, doubleSided; }
    [Serializable] public class Character
    {
        public string key, displayName, spawn, idle, attack, hit;
        public int cardId;
        public MaterialInfo[] materials;
    }
    [Serializable] public class Manifest { public Character[] characters; }
    [Serializable] public class CardAssignment { public int id, cost, attack, health; public string faction, name, reference; }
    [Serializable] public class CardAssignments { public CardAssignment[] cards; }

    public static void VerifyCardAssignments()
    {
        var expected = JsonUtility.FromJson<CardAssignments>("{\"cards\":" + File.ReadAllText(Arg("-cardAssignments")) + "}");
        var database = AssetDatabase.LoadAssetAtPath<CardDatabase>("Assets/2. Data/Card/CardDatabase.asset");
        if (expected.cards.Length != 27 || database.allCards.Count != 27) throw new Exception("Expected 27 cards");
        foreach (var row in expected.cards)
        {
            var card = database.GetCardById(row.id);
            if (!card || card.cardName != row.faction + "-" + row.name || card.cost != row.cost || card.attack != row.attack || card.health != row.health)
                throw new Exception("Spreadsheet card mismatch: " + row.id);
            var guid = System.Text.RegularExpressions.Regex.Match(row.reference, "guid: ([a-f0-9]+)");
            if (guid.Success)
            {
                string actual = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(card.hologramPrefab));
                if (actual != guid.Groups[1].Value) throw new Exception("Model assignment mismatch: " + row.id);
            }
            else if (card.hologramPrefab) throw new Exception("Unassigned card unexpectedly has a model: " + row.id);
        }
        ProductionGameVerification.CompleteChecks();
        Debug.Log("SPREADSHEET_CARD_ASSIGNMENTS_SUCCESS: all 27 names, numbers, stats and model references match; HP25; production and network checks passed");
    }

    public static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name);
        return args[index + 1];
    }
    public static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Arg("-characterManifest")));
    static string ModelPath(Character c, string action) => Root + "/Models/" + c.key + "/" + action + ".fbx";
    static AnimationClip Clip(Character c, string action) => AssetDatabase.LoadAllAssetsAtPath(ModelPath(c, action))
        .OfType<AnimationClip>().Single(a => !a.name.StartsWith("__preview__"));

    static void Configure(Character c, string action, Avatar avatar)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath(c, action));
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = avatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        importer.importCameras = importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        var clips = importer.defaultClipAnimations;
        if (clips.Length != 1) throw new Exception(c.key + ": expected one animation per FBX");
        clips[0].name = action;
        clips[0].loopTime = action == c.idle;
        clips[0].loopPose = false;
        clips[0].lockRootRotation = clips[0].lockRootHeightY = clips[0].lockRootPositionXZ = true;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    public static void Build()
    {
        var manifest = ReadManifest();
        foreach (string folder in new[] { "Prefabs", "Materials", "Controllers", "Scenes" }) Directory.CreateDirectory(Root + "/" + folder);
        AssetDatabase.Refresh();
        var prefabs = manifest.characters.Select(BuildCharacter).ToList();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var group = new GameObject("New characters - Preview context menu");
        group.AddComponent<CharacterPreview>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            var obj = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
            obj.transform.SetParent(group.transform, false);
            obj.transform.localPosition = new Vector3((i - 1) * 1.65f, 0, 0);
        }
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 2.1f, -5);
        camera.transform.LookAt(new Vector3(0, .4f, 0));
        camera.orthographic = true;
        camera.orthographicSize = 1.25f;
        camera.backgroundColor = new Color(.09f, .11f, .14f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.SaveScene(scene, Root + "/Scenes/AdditionalCharacterPreview.unity");
        AssetDatabase.SaveAssets();
        Validate();
        Capture();
        Debug.Log("ADDITIONAL_CHARACTER_BUILD_SUCCESS: three prefabs; twelve motion states; cards " + string.Join(",", manifest.characters.Select(c => c.cardId)));
    }

    static GameObject BuildCharacter(Character c)
    {
        Configure(c, c.spawn, null);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath(c, c.spawn)).OfType<Avatar>().Single();
        foreach (string action in new[] { c.idle, c.attack, c.hit }) Configure(c, action, avatar);
        string controllerPath = Root + "/Controllers/" + c.key + ".controller";
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)) AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        string[] names = { "Spawn", "Idle", "Attack", "Hit" };
        string[] actions = { c.spawn, c.idle, c.attack, c.hit };
        var states = new Dictionary<string, AnimatorState>();
        for (int i = 0; i < names.Length; i++)
        {
            var state = machine.AddState(names[i], new Vector3(i * 220, 80));
            state.motion = Clip(c, actions[i]);
            state.writeDefaultValues = false;
            states.Add(names[i], state);
        }
        machine.defaultState = states["Spawn"];
        foreach (string name in new[] { "Spawn", "Attack", "Hit" })
        {
            var transition = states[name].AddTransition(states["Idle"]);
            transition.hasExitTime = true; transition.exitTime = 1;
            transition.hasFixedDuration = true; transition.duration = .05f;
        }
        foreach (string name in new[] { "Attack", "Hit" })
        {
            var transition = machine.AddAnyStateTransition(states[name]);
            transition.hasExitTime = false; transition.hasFixedDuration = true;
            transition.duration = .05f; transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0, name);
        }
        var materials = new Dictionary<string, Material>();
        foreach (var info in c.materials)
        {
            string texturePath = Root + "/Models/" + c.key + "/" + info.texture;
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.maxTextureSize = 1024;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) throw new Exception("Missing texture: " + texturePath);
            string matPath = Root + "/Materials/" + c.key + "_" + info.name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.name = info.name;
            mat.SetTexture("_BaseMap", texture); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", info.transparent ? 1 : 0); mat.SetFloat("_Cutoff", .3f);
            if (info.transparent) { mat.EnableKeyword("_ALPHATEST_ON"); mat.renderQueue = 2450; }
            mat.SetFloat("_Cull", info.doubleSided ? 0 : 2);
            EditorUtility.SetDirty(mat); materials.Add(info.name, mat);
        }
        var wrapper = new GameObject(c.key);
        var facing = new GameObject("Facing").transform;
        facing.SetParent(wrapper.transform, false); facing.localRotation = Quaternion.Euler(0, 180, 0);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(c, c.spawn)));
        visual.transform.SetParent(facing, false);
        foreach (var r in visual.GetComponentsInChildren<Renderer>(true))
        {
            r.sharedMaterials = r.sharedMaterials.Select(m => m != null && materials.TryGetValue(m.name, out var mat) ? mat : throw new Exception("Unmapped material: " + m)).ToArray();
            if (r is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
        var animator = visual.GetComponent<Animator>();
        if (!animator) throw new Exception("Missing Animator: " + c.key);
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var motion = wrapper.AddComponent<CharacterMotion>();
        motion.animator = animator; motion.attackClip = Clip(c, c.attack); motion.hitClip = Clip(c, c.hit);
        animator.Rebind(); animator.Play("Idle", 0, .25f); animator.Update(0);
        var bounds = BakedBounds(wrapper);
        float scale = Mathf.Min(.85f / bounds.size.y, 1f / bounds.size.x, 1.1f / bounds.size.z);
        facing.localScale = Vector3.one * scale;
        facing.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Root + "/Prefabs/" + c.key + ".prefab");
        UnityEngine.Object.DestroyImmediate(wrapper);
        var card = AssetDatabase.FindAssets("t:CardData", new[] { "Assets/2. Data/Card" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<CardData>).Single(x => x.cardId == c.cardId);
        if (card.cardType != CardType.Unit || (card.hologramPrefab && card.hologramPrefab != prefab)) throw new Exception("Card is already assigned: " + c.cardId);
        card.hologramPrefab = prefab; EditorUtility.SetDirty(card);
        Debug.Log("ADDITIONAL_CHARACTER_CARD: " + c.cardId + " -> " + c.displayName + "; idle bounds " + bounds + "; scale " + scale);
        return prefab;
    }

    public static Bounds BakedBounds(GameObject obj)
    {
        bool first = true; var result = new Bounds(); var mesh = new Mesh();
        foreach (var skin in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            skin.BakeMesh(mesh, true);
            foreach (var vertex in mesh.vertices)
            {
                var point = skin.transform.TransformPoint(vertex);
                if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
            }
        }
        UnityEngine.Object.DestroyImmediate(mesh);
        if (first || result.size.sqrMagnitude < .001f) throw new Exception("Empty skinned geometry: " + obj.name);
        return result;
    }

    public static void Validate()
    {
        foreach (var c in ReadManifest().characters)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + c.key + ".prefab");
            var motion = prefab.GetComponent<CharacterMotion>();
            var controller = (AnimatorController)motion.animator.runtimeAnimatorController;
            var machine = controller.layers[0].stateMachine;
            if (machine.states.Length != 4 || controller.parameters.Length != 2 || machine.defaultState.name != "Spawn") throw new Exception("Invalid controller: " + c.key);
            foreach (var s in machine.states)
            {
                var clip = (AnimationClip)s.state.motion;
                if (clip.length <= 0 || AnimationUtility.GetCurveBindings(clip).Length == 0) throw new Exception("Empty animation: " + c.key + "/" + s.state.name);
            }
            var instance = UnityEngine.Object.Instantiate(prefab);
            var animator = instance.GetComponentInChildren<Animator>();
            animator.Rebind(); animator.Play("Idle", 0, .25f); animator.Update(0);
            var bounds = BakedBounds(instance);
            if (bounds.size.y > .86f || bounds.size.x > 1.01f || bounds.size.z > 1.11f || Mathf.Abs(bounds.min.y) > .01f) throw new Exception("Invalid field size: " + c.key + " " + bounds);
            foreach (var r in instance.GetComponentsInChildren<Renderer>())
                if (r.sharedMaterials.Any(m => !m || m.GetTexture("_BaseMap") == null)) throw new Exception("Missing material: " + c.key);
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("ADDITIONAL_CHARACTER_STATIC_PASS: " + c.key + "; " + bounds.size);
        }
    }

    public static void Capture()
    {
        EditorSceneManager.OpenScene(Root + "/Scenes/AdditionalCharacterPreview.unity");
        foreach (var motion in UnityEngine.Object.FindObjectsByType<CharacterMotion>(FindObjectsSortMode.None))
        {
            motion.animator.Rebind(); motion.animator.Play("Idle", 0, .25f); motion.animator.Update(0);
        }
        var camera = Camera.main;
        var target = new RenderTexture(1800, 720, 24);
        camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
        var image = new Texture2D(1800, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1800, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Arg("-characterPreview"), image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("ADDITIONAL_CHARACTER_PREVIEW_SAVED");
    }
}

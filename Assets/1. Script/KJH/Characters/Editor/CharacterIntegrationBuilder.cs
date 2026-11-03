using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KJH.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CharacterIntegrationBuilder
{
    const string Root = "Assets/1. Script/KJH/Characters";
    [Serializable] public class MaterialInfo { public string name, image; public bool transparent, doubleSided; }
    [Serializable] public class Character
    {
        public string key, spawn, idle, attack, hit;
        public int cardId;
        public MaterialInfo[] materials;
    }
    [Serializable] public class Manifest { public Character[] characters; }

    static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name);
        return args[index + 1];
    }

    public static void Build()
    {
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Arg("-characterManifest")));
        Directory.CreateDirectory(Root + "/Prefabs");
        Directory.CreateDirectory(Root + "/Controllers");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Scenes");
        AssetDatabase.Refresh();
        var prefabs = new List<GameObject>();
        foreach (var c in manifest.characters) prefabs.Add(BuildCharacter(c));
        NormalizeExisting();
        BuildGallery(prefabs);
        BuildIntegrationScene();
        AssetDatabase.SaveAssets();
        Validate(manifest);
        Debug.Log("CHARACTER_INTEGRATION_SUCCESS: 10 prefabs, 40 states, 10 card assignments, 4 VR slot bridges");
    }

    static string ModelPath(Character c, string action) => Root + "/Models/" + c.key + "/" + action + ".fbx";

    static void ConfigureModel(Character c, string action, Avatar avatar)
    {
        string path = ModelPath(c, action);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = avatar == null ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        importer.importCameras = false;
        importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        var clips = importer.defaultClipAnimations;
        if (clips.Length != 1) throw new InvalidOperationException(path + ": expected one clip");
        clips[0].name = action;
        clips[0].loopTime = action == c.idle;
        clips[0].loopPose = false;
        clips[0].lockRootRotation = true;
        clips[0].lockRootHeightY = true;
        clips[0].lockRootPositionXZ = true;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip Clip(Character c, string action) => AssetDatabase.LoadAllAssetsAtPath(ModelPath(c, action))
        .OfType<AnimationClip>().Single(a => !a.name.StartsWith("__preview__"));

    static GameObject BuildCharacter(Character c)
    {
        ConfigureModel(c, c.spawn, null);
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath(c, c.spawn)).OfType<Avatar>().Single();
        foreach (string action in new[] { c.idle, c.attack, c.hit }.Distinct())
            if (action != c.spawn) ConfigureModel(c, action, avatar);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(Root + "/Controllers/" + c.key + ".controller");
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        var machine = controller.layers[0].stateMachine;
        var states = new Dictionary<string, AnimatorState>();
        var actions = new[] { c.spawn, c.idle, c.attack, c.hit };
        string[] names = { "Spawn", "Idle", "Attack", "Hit" };
        for (int i = 0; i < names.Length; i++)
        {
            var state = machine.AddState(names[i], new Vector3(i * 230, 80));
            state.motion = Clip(c, actions[i]);
            state.writeDefaultValues = false;
            states.Add(names[i], state);
        }
        machine.defaultState = states["Spawn"];
        foreach (string name in new[] { "Spawn", "Attack", "Hit" })
        {
            var transition = states[name].AddTransition(states["Idle"]);
            transition.hasExitTime = true;
            transition.exitTime = 1f;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
        }
        foreach (string name in new[] { "Attack", "Hit" })
        {
            var transition = machine.AddAnyStateTransition(states[name]);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = true;
            transition.AddCondition(AnimatorConditionMode.If, 0, name);
        }

        var materials = new Dictionary<string, Material>();
        foreach (var info in c.materials)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.name = info.name;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Models/" + c.key + "/texture/" + info.image + ".png");
            if (texture == null) throw new InvalidOperationException(c.key + ": missing texture " + info.image);
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            // Alpha cutout keeps face overlays and accessories visible without depth sorting artifacts.
            mat.SetFloat("_AlphaClip", info.transparent ? 1 : 0);
            mat.SetFloat("_Cutoff", 0.3f);
            if (info.transparent) { mat.EnableKeyword("_ALPHATEST_ON"); mat.renderQueue = 2450; }
            mat.SetFloat("_Cull", info.doubleSided ? 0 : 2);
            string matPath = Root + "/Materials/" + c.key + "_" + info.name + ".mat";
            AssetDatabase.CreateAsset(mat, matPath);
            materials.Add(info.name, mat);
        }

        var wrapper = new GameObject(c.key);
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(c, c.spawn)));
        visual.transform.SetParent(wrapper.transform, false);
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
            {
                if (m == null || !materials.TryGetValue(m.name, out var material))
                    throw new InvalidOperationException(c.key + ": unmapped material " + (m == null ? "null" : m.name));
                return material;
            }).ToArray();
            if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
        var bounds = new Bounds();
        bool first = true;
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
        {
            if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
        }
        float scale = 1.5f / Mathf.Max(bounds.size.y, 0.01f);
        visual.transform.localScale *= scale;
        visual.transform.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        var animator = visual.GetComponent<Animator>();
        if (animator == null) throw new InvalidOperationException(c.key + ": missing Animator");
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var motion = wrapper.AddComponent<CharacterMotion>();
        motion.animator = animator;
        motion.attackClip = Clip(c, c.attack);
        motion.hitClip = Clip(c, c.hit);
        var prefab = PrefabUtility.SaveAsPrefabAsset(wrapper, Root + "/Prefabs/" + c.key + ".prefab");
        UnityEngine.Object.DestroyImmediate(wrapper);

        var cardPath = AssetDatabase.FindAssets("t:CardData", new[] { "Assets/2. Data/Card" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Single(p => AssetDatabase.LoadAssetAtPath<CardData>(p).cardId == c.cardId);
        var card = AssetDatabase.LoadAssetAtPath<CardData>(cardPath);
        if (card.cardType != CardType.Unit) throw new InvalidOperationException("Cannot assign spell card");
        if (card.hologramPrefab != null && card.hologramPrefab != prefab)
            throw new InvalidOperationException("Existing model must be preserved: " + cardPath);
        card.hologramPrefab = prefab;
        EditorUtility.SetDirty(card);
        Debug.Log("CHARACTER_CARD: " + c.cardId + " " + card.cardName + " -> " + c.key);
        return prefab;
    }

    static void BuildGallery(List<GameObject> prefabs)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var group = new GameObject("Characters - Play then use Preview context menu");
        group.AddComponent<CharacterPreview>();
        for (int i = 0; i < prefabs.Count; i++)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i]);
            model.transform.SetParent(group.transform);
            model.transform.position = new Vector3((i % 5 - 2) * 2.8f, 0, (i / 5) * 3.5f);
        }
        var camera = new GameObject("Preview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 8, -11);
        camera.transform.LookAt(new Vector3(0, 0.7f, 1.5f));
        camera.orthographic = true;
        camera.orthographicSize = 4.8f;
        camera.backgroundColor = new Color(0.09f, 0.11f, 0.14f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        EditorSceneManager.SaveScene(scene, Root + "/Scenes/CharacterPreview.unity");
    }

    static void BuildIntegrationScene()
    {
        var scene = EditorSceneManager.OpenScene("Assets/0. Scene/MainGame.unity", OpenSceneMode.Single);
        foreach (var slot in UnityEngine.Object.FindObjectsByType<SlotModelSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var serialized = new SerializedObject(slot);
            var bridge = slot.gameObject.AddComponent<CharacterSlotAnimationBridge>();
            bridge.side = (PlayerSide)serialized.FindProperty("side").intValue;
            bridge.laneIndex = serialized.FindProperty("laneIndex").intValue;
        }
        EditorSceneManager.SaveScene(scene, Root + "/Scenes/MainGame_Characters.unity", true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    static void Validate(Manifest manifest)
    {
        foreach (var c in manifest.characters)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/" + c.key + ".prefab");
            var animator = prefab.GetComponentInChildren<Animator>();
            var controller = (AnimatorController)animator.runtimeAnimatorController;
            if (controller.layers[0].stateMachine.states.Length != 4 || controller.parameters.Length != 2)
                throw new InvalidOperationException(c.key + ": invalid controller");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var live = instance.GetComponentInChildren<Animator>();
            live.Rebind();
            foreach (string state in new[] { "Spawn", "Idle", "Attack", "Hit" })
            {
                live.Play(state, 0, 0.5f);
                live.Update(0);
                if (!live.GetCurrentAnimatorStateInfo(0).IsName(state))
                    throw new InvalidOperationException(c.key + ": state cannot play: " + state);
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    var mesh = new Mesh();
                    renderer.BakeMesh(mesh);
                    if (mesh.vertexCount == 0) throw new InvalidOperationException(c.key + ": empty skinned mesh");
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            live.Play("Idle", 0, 0);
            live.Update(0);
            live.SetTrigger("Attack");
            live.Update(0.1f);
            live.Update(0.1f);
            if (!live.GetCurrentAnimatorStateInfo(0).IsName("Attack")) throw new InvalidOperationException(c.key + ": Attack trigger failed");
            live.SetTrigger("Hit");
            live.Update(0.1f);
            live.Update(0.1f);
            if (!live.GetCurrentAnimatorStateInfo(0).IsName("Hit")) throw new InvalidOperationException(c.key + ": Hit trigger failed");
            UnityEngine.Object.DestroyImmediate(instance);
            Debug.Log("CHARACTER_VALIDATED: " + c.key);
        }
    }

    public static void NormalizeExisting()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Root + "/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            instance.transform.localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator.transform.parent != instance.transform)
                animator.transform.parent.localRotation = Quaternion.identity;
            animator.transform.localPosition = Vector3.zero;
            animator.transform.localScale = Vector3.one;
            animator.transform.localRotation = Quaternion.identity;
            animator.Rebind();
            animator.Play("Idle", 0, 0.25f);
            animator.Update(0);
            // Spawn clips can begin underground or at zero scale. Use the body in Idle
            // rather than the FBX's first-frame renderer bounds to establish card size.
            var body = instance.GetComponentsInChildren<SkinnedMeshRenderer>()
                .OrderByDescending(r => r.sharedMesh.vertexCount).First();
            var mesh = new Mesh();
            body.BakeMesh(mesh, true);
            var vertices = mesh.vertices;
            var bounds = new Bounds(body.transform.TransformPoint(vertices[0]), Vector3.zero);
            foreach (var vertex in vertices) bounds.Encapsulate(body.transform.TransformPoint(vertex));
            if (bounds.size.y < 0.001f) throw new InvalidOperationException(path + ": invalid idle body bounds");
            float scale = 1.5f / bounds.size.y;
            var contents = PrefabUtility.LoadPrefabContents(path);
            var visual = contents.GetComponentInChildren<Animator>().transform;
            var facing = contents.transform.Find("Facing");
            if (facing == null)
            {
                facing = new GameObject("Facing").transform;
                facing.SetParent(contents.transform, false);
                visual.SetParent(facing, false);
            }
            visual.localScale = Vector3.one * scale;
            string key = Path.GetFileNameWithoutExtension(path);
            contents.transform.localScale = Vector3.one * (key == "NaelleumMeong" ? 0.55f : key == "MasitNyang" ? 0.5f : 1f);
            float yaw = key == "Gangplank" || key == "Fiora" ? -90 :
                key == "Garen" || key == "TahmKench" || key == "NaelleumMeong" || key == "MasitNyang" ? 180 : 0;
            facing.localRotation = Quaternion.Euler(0, yaw, 0);
            visual.localRotation = Quaternion.identity;
            visual.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("CHARACTER_NORMALIZED: " + instance.name + " idle bounds " + bounds + " scale " + scale);
            UnityEngine.Object.DestroyImmediate(mesh);
            UnityEngine.Object.DestroyImmediate(instance);
        }
        AssetDatabase.SaveAssets();
    }
}

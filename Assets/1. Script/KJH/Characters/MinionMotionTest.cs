using System;
using System.Linq;
using UnityEngine;

namespace KJH.Characters
{
    public sealed class MinionMotionTest : MonoBehaviour
    {
        [SerializeField] CardDatabase database;
        [SerializeField] Font font;

        enum Motion { Spawn, Idle, Attack, Hit }

        sealed class Entry
        {
            public CardData card;
            public Transform stand;
            public GameObject model;
            public CharacterMotion motion;
            public Animator animator;
        }

        const float ColumnSpacing = 1.9f, RowSpacing = 1.7f;
        const float PanelWidth = 260f, HeaderHeight = 86f;
        readonly Color background = new Color(0.055f, 0.08f, 0.11f);
        Entry[] entries;
        Camera previewCamera;
        Vector2 scroll;
        int selected = -1;
        int columns;
        float rotation;
        GUIStyle heading, label, button, caption;

        void Start()
        {
            if (database == null || font == null)
            {
                Debug.LogError("하수인 모션 테스트 씬에 카드 데이터와 글꼴을 연결하세요.", this);
                enabled = false;
                return;
            }

            var cameraObject = new GameObject("Motion Test Camera", typeof(Camera));
            cameraObject.transform.SetParent(transform, false);
            previewCamera = cameraObject.GetComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = background;
            previewCamera.orthographic = true;
            previewCamera.stereoTargetEye = StereoTargetEyeMask.None;
            previewCamera.farClipPlane = 50f;
            var light = new GameObject("Motion Test Light", typeof(Light));
            light.transform.SetParent(transform, false);
            light.transform.rotation = Quaternion.Euler(32f, -25f, 0f);
            light.GetComponent<Light>().type = LightType.Directional;

            entries = database.allCards.Where(card => card != null && card.cardType == CardType.Unit)
                .OrderBy(card => card.cardId).Select(card =>
                {
                    var stand = new GameObject($"{card.cardId:00} {card.cardName}").transform;
                    stand.SetParent(transform, false);
                    return new Entry { card = card, stand = stand };
                }).ToArray();
            foreach (var entry in entries) Spawn(entry);
            Show(-1);
        }

        void Spawn(Entry entry)
        {
            if (entry.card.hologramPrefab == null) return;
            if (entry.model != null)
            {
                entry.model.SetActive(false);
                Destroy(entry.model);
            }

            entry.model = Instantiate(entry.card.hologramPrefab, entry.stand);
            entry.motion = entry.model.GetComponentInChildren<CharacterMotion>();
            entry.animator = entry.motion != null ? entry.motion.animator : entry.model.GetComponentInChildren<Animator>();
            FrameModel(entry);
        }

        static void FrameModel(Entry entry)
        {
            var renderers = entry.model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var standRotation = entry.stand.localRotation;
            entry.stand.localRotation = Quaternion.identity;
            var model = entry.model.transform;
            // The gallery camera sees the models from the opposite side of some game slots.
            float yaw = entry.card.cardId switch
            {
                13 or 20 or 21 => 180f,
                14 => -65f,
                22 => 115f,
                _ => 0f,
            };
            model.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.localRotation;

            var animator = entry.animator;
            bool sampleIdle = animator != null && animator.HasState(0, Animator.StringToHash("Base Layer.Idle"));
            if (sampleIdle)
            {
                animator.Play("Base Layer.Idle", 0, 0.25f);
                animator.Update(0f);
            }
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = 1.05f / Mathf.Max(bounds.size.x, bounds.size.y, 0.01f);
            var centerOffset = entry.stand.InverseTransformPoint(bounds.center);
            model.localScale *= scale;
            model.localPosition = (model.localPosition - centerOffset) * scale;

            if (sampleIdle)
            {
                animator.Play("Base Layer.Spawn", 0, 0f);
                animator.Update(0f);
            }
            entry.stand.localRotation = standRotation;
        }

        void Show(int index)
        {
            selected = index;
            rotation = 0f;
            foreach (var entry in entries) entry.stand.localRotation = Quaternion.identity;
            LayoutPreview();
        }

        void LateUpdate() => LayoutPreview();

        void LayoutPreview()
        {
            if (entries == null) return;
            float panel = Mathf.Min(PanelWidth, Screen.width * 0.42f);
            float header = Mathf.Min(HeaderHeight, Screen.height * 0.28f);
            previewCamera.pixelRect = new Rect(panel, 0f, Mathf.Max(1, Screen.width - panel), Mathf.Max(1, Screen.height - header));
            float aspect = previewCamera.pixelWidth / (float)previewCamera.pixelHeight;
            columns = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(entries.Length * aspect * RowSpacing / ColumnSpacing)), 2, 7);
            int rows = Mathf.Max(1, Mathf.CeilToInt(entries.Length / (float)columns));
            for (int i = 0; i < entries.Length; i++)
            {
                var stand = entries[i].stand;
                bool visible = selected == -1 || selected == i;
                if (stand.gameObject.activeSelf != visible) stand.gameObject.SetActive(visible);
                if (!visible) continue;
                stand.localPosition = selected >= 0 ? Vector3.zero : new Vector3(
                    (i % columns - (columns - 1) * 0.5f) * ColumnSpacing,
                    ((rows - 1) * 0.5f - i / columns) * RowSpacing, 0f);
            }
            previewCamera.transform.localPosition = new Vector3(0f, -0.14f, -15f);
            previewCamera.orthographicSize = selected >= 0 ? Mathf.Max(0.95f, 0.95f / aspect) :
                Mathf.Max(rows * RowSpacing, columns * ColumnSpacing / aspect) * 0.53f;
        }

        static bool CanPlay(Entry entry, Motion motion)
        {
            if (entry.model == null) return false;
            if (motion == Motion.Spawn) return true;
            if (motion == Motion.Idle) return entry.animator != null && entry.animator.HasState(0, Animator.StringToHash("Base Layer.Idle"));
            return entry.motion != null && entry.animator != null && !entry.motion.IsPlaying;
        }

        void Play(Motion action)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (selected >= 0 && i != selected) continue;
                var entry = entries[i];
                if (!CanPlay(entry, action)) continue;
                switch (action)
                {
                    case Motion.Spawn: Spawn(entry); break;
                    case Motion.Idle:
                        if (entry.motion != null && entry.motion.IsPlaying) Spawn(entry);
                        entry.animator.Play("Base Layer.Idle", 0, 0f);
                        break;
                    case Motion.Attack: entry.motion.Attack(); break;
                    case Motion.Hit: entry.motion.Hit(); break;
                }
            }
        }

        void OnGUI()
        {
            if (entries == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 15, clipping = TextClipping.Clip };
                heading = new GUIStyle(label) { fontSize = 19, fontStyle = FontStyle.Bold };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 15, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(9, 9, 0, 0) };
                caption = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            }

            var viewport = previewCamera.pixelRect;
            float panel = viewport.xMin;
            float header = Screen.height - viewport.height;
            GUI.Box(new Rect(0, 0, panel, Screen.height), GUIContent.none);
            GUI.Box(new Rect(panel, 0, viewport.width, header), GUIContent.none);
            GUILayout.BeginArea(new Rect(10, 8, panel - 20, Screen.height - 16));
            GUILayout.Label("하수인 모션 테스트", heading);
            GUILayout.Label($"전체 {entries.Length}종 · 모델 {entries.Count(e => e.card.hologramPrefab != null)}종", label);
            if (GUILayout.Button("전체 보기", button, GUILayout.Height(28))) Show(-1);
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < entries.Length; i++)
            {
                var card = entries[i].card;
                string status = card.hologramPrefab == null ? " · 미배정" : "";
                if (GUILayout.Button($"{(selected == i ? "▸ " : "")}{card.cardId:00}  {card.cardName}{status}", button, GUILayout.Height(25))) Show(i);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(panel + 10, 8, viewport.width - 20, header));
            string title = selected < 0 ? "전체 하수인" : entries[selected].card.cardName;
            GUILayout.Label(title, heading);
            GUILayout.BeginHorizontal();
            MotionButton("등장", Motion.Spawn);
            MotionButton("대기", Motion.Idle);
            MotionButton("공격", Motion.Attack);
            MotionButton("피격", Motion.Hit);
            if (selected >= 0 && entries[selected].model != null)
            {
                GUILayout.Space(8f);
                GUILayout.BeginVertical(GUILayout.MaxWidth(240f));
                rotation = GUILayout.HorizontalSlider(rotation, -180f, 180f);
                GUILayout.BeginHorizontal();
                GUILayout.Label("왼쪽 -180°", caption, GUILayout.Width(68f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("정면 0°", caption, GUILayout.Width(55f))) rotation = 0f;
                GUILayout.FlexibleSpace();
                GUILayout.Label("오른쪽 180°", caption, GUILayout.Width(68f));
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                entries[selected].stand.localRotation = Quaternion.Euler(0f, rotation, 0f);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (selected < 0)
            {
                foreach (var entry in entries)
                {
                    var point = previewCamera.WorldToScreenPoint(entry.stand.position - Vector3.up * 0.67f);
                    var card = entry.card;
                    string name = card.hologramPrefab == null ? $"{card.cardId:00} {card.cardName} · 미배정" : $"{card.cardId:00} {card.cardName}";
                    float width = Mathf.Min(185f, ColumnSpacing * viewport.height / (2f * previewCamera.orthographicSize));
                    var rect = new Rect(point.x - width * 0.5f, Screen.height - point.y, width, 22f);
                    if (GUI.Button(rect, name, caption)) Show(Array.IndexOf(entries, entry));
                }
            }
            else if (entries[selected].model == null)
                GUI.Label(new Rect(panel, header + (viewport.height - 25) * 0.5f, viewport.width, 25), "미배정 · 연결된 모델 없음", caption);
        }

        void MotionButton(string text, Motion action)
        {
            bool previous = GUI.enabled;
            GUI.enabled = selected < 0 ? entries.Any(entry => CanPlay(entry, action)) : CanPlay(entries[selected], action);
            if (GUILayout.Button(text, button, GUILayout.MinWidth(48), GUILayout.Height(28))) Play(action);
            GUI.enabled = previous;
        }
    }
}

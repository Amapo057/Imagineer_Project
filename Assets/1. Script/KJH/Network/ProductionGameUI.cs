using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR;

// World-space, head-gaze controls work even when no controller/hand-ray setup is present.
public sealed class ProductionGameUI : MonoBehaviour
{
    public TMP_FontAsset font;
    public Camera viewCamera;
    public float dwellSeconds = 1.5f;
    public bool Built { get; private set; }
    public readonly Dictionary<string, UnityEngine.UI.Button> Buttons = new Dictionary<string, UnityEngine.UI.Button>();
    readonly List<UnityEngine.UI.Button> gameButtons = new List<UnityEngine.UI.Button>();
    readonly List<RaycastResult> hits = new List<RaycastResult>();
    ProductionGameController session;
    RectTransform panel;
    GameObject connectionPanel, gameplayPanel;
    TextMeshProUGUI statusText, actionText, fieldsText, gazeText, ipText;
    UnityEngine.UI.GraphicRaycaster raycaster;
    UnityEngine.UI.Button looked, lastActivated;
    float held, refreshAt;
    bool triggerWasDown, lastReady, placedForXR;
    string hostIp = "";
    const string SavedMode = "KJH.Game.Mode", SavedIP = "KJH.Game.HostIP";
    static readonly Color Ink = new Color(0.90f, 0.95f, 1f), Background = new Color(0.035f, 0.065f, 0.12f, 1f);

    void Start() => BuildNow();
    public void BuildNow()
    {
        if (Built) return;
        session = GetComponent<ProductionGameController>(); session.Initialize();
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || font == null) throw new System.InvalidOperationException("게임 카메라/한글 폰트가 없습니다. 실제 게임 적용 씬을 다시 생성하세요.");
        if (EventSystem.current == null)
        {
            var events = new GameObject("Game EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        var canvasObject = new GameObject("Game Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = viewCamera;
        canvas.sortingOrder = 100;
        panel = canvasObject.GetComponent<RectTransform>(); panel.sizeDelta = new Vector2(1100, 1180); panel.localScale = Vector3.one * 0.0013f;
        var background = canvasObject.AddComponent<UnityEngine.UI.Image>(); background.color = Background; background.raycastTarget = false;
        var layout = canvasObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 18, 18); layout.spacing = 8;
        layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        raycaster = canvasObject.GetComponent<UnityEngine.UI.GraphicRaycaster>();
        Label(panel, "Title", "이매지니어", 34, 46);
        Label(panel, "Build", "빌드 " + session.BuildLabel + " / 데이터 " + session.Network.MatchSignature, 19, 30);
        Label(panel, "Instructions", "머리를 움직여 흰 점을 버튼에 맞추고 1.5초 기다리세요.\n선택 뒤 다른 곳을 보세요. 컨트롤러 트리거로 즉시 선택도 가능합니다.", 21, 58);
        statusText = Label(panel, "Status", "", 21, 200);
        var tabs = Row(panel, "Tabs", 58);
        Button(tabs, "ConnectionTab", "연결 설정", () => ShowConnection(true));
        Button(tabs, "GameplayTab", "게임 메뉴", () => ShowConnection(false));
        connectionPanel = Vertical(panel, "Connection", 486).gameObject;
        var connectRow = Row(connectionPanel.transform, "ConnectionModes", 60);
        Button(connectRow, "Auto", "자동 검색", () => { SaveMode(GameNetworkManager.ConnectionMode.Automatic); session.Network.StartAutomatic(); });
        Button(connectRow, "Host", "내가 수동 호스트", () => { SaveMode(GameNetworkManager.ConnectionMode.Host); session.Network.StartManualHost(); });
        Button(connectRow, "Retry", "다시 연결", () => session.Network.RetryConnection());
        var ipRow = Row(connectionPanel.transform, "IP Row", 44);
        ipRow.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
        ipText = Label(ipRow, "HostIP", "호스트 IP: 숫자 키로 입력", 25, 44);
        ipText.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        var clear = Button(ipRow, "ClearIP", "전체 지우기", () => { hostIp = ""; ipText.text = "호스트 IP: 숫자 키로 입력"; });
        clear.GetComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 180;
        clear.GetComponent<UnityEngine.UI.LayoutElement>().minHeight = 44;
        clear.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = 44;
        var keypad = Grid(connectionPanel.transform, "IP Keypad", 218, 3, 48);
        foreach (string key in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "지우기" })
        {
            string captured = key;
            Button(keypad, "Key" + (key == "." ? "Dot" : key), key, () =>
            {
                if (captured == "지우기") { if (hostIp.Length > 0) hostIp = hostIp.Substring(0, hostIp.Length - 1); }
                else if (hostIp.Length < 15) hostIp += captured;
                ipText.text = "호스트 IP: " + hostIp;
            });
        }
        Button(connectionPanel.transform, "ConnectIP", "입력한 호스트 IP로 연결", () =>
        {
            if (session.Network.ConnectToHost(hostIp)) SaveMode(GameNetworkManager.ConnectionMode.Client);
        });
        Label(connectionPanel.transform, "ManualHint", "수동 연결: 한 기기는 ‘호스트’, 다른 기기는 그 기기의 Wi-Fi IP 입력.\n호스트와 선공은 별개입니다. 같은 APK를 실행하고 서로의 턴·체력·필드를 확인하세요.", 20, 60);

        gameplayPanel = Vertical(panel, "Gameplay", 434).gameObject;
        fieldsText = Label(gameplayPanel.transform, "Fields", "", 21, 105);
        var actions = Grid(gameplayPanel.transform, "Game Actions", 202, 3, 62);
        gameButtons.Add(Button(actions, "EndTurn", "턴 종료", session.EndTurn));
        Button(actions, "Coin", "후공 동전 사용", session.UseCoin);
        Button(actions, "DrawConfirm", "뽑기 1장 확인", session.ConfirmDraw);
        Button(actions, "Rematch", "새 판 / 재대결", session.Rematch);
        Label(gameplayPanel.transform, "Scope", "하수인과 마법은 실물 카드를 필드 슬롯에 놓아 사용합니다.\n코스트 부족·상대 턴·미연결 상태에서는 카드가 등록되지 않습니다.\n새 판에서는 필드의 실물 카드를 모두 치운 뒤 다시 놓으세요.", 21, 110);
        actionText = Label(panel, "Action", "", 22, 65);
        gazeText = Label(panel, "Gaze", "버튼을 바라보세요.", 20, 30);
        AddReticle(); Built = true; ShowConnection(true); PlacePanel(); Refresh();
        if (Application.platform == RuntimePlatform.Android)
        {
            hostIp = PlayerPrefs.GetString(SavedIP, ""); if (hostIp.Length > 0) ipText.text = "호스트 IP: " + hostIp;
            var mode = (GameNetworkManager.ConnectionMode)PlayerPrefs.GetInt(SavedMode, 0);
            if (mode == GameNetworkManager.ConnectionMode.Host) session.Network.StartManualHost();
            else if (mode == GameNetworkManager.ConnectionMode.Client && hostIp.Length > 0) session.Network.ConnectToHost(hostIp);
        }
    }

    void SaveMode(GameNetworkManager.ConnectionMode mode)
    {
        if (Application.platform != RuntimePlatform.Android) return;
        PlayerPrefs.SetInt(SavedMode, (int)mode); PlayerPrefs.SetString(SavedIP, hostIp); PlayerPrefs.Save();
    }

    void AddReticle()
    {
        var root = new GameObject("Gaze Reticle", typeof(RectTransform), typeof(Canvas)); root.transform.SetParent(viewCamera.transform, false);
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rect = root.GetComponent<RectTransform>(); rect.localPosition = Vector3.forward * 1.9f; rect.localScale = Vector3.one * 0.0014f; rect.sizeDelta = new Vector2(6, 6);
        var dot = root.AddComponent<UnityEngine.UI.Image>(); dot.color = Color.white; dot.raycastTarget = false;
    }
    public void PlacePanel()
    {
        Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
        panel.position = viewCamera.transform.position + forward * 2f - Vector3.up * 0.06f;
        if (session.Ready && !connectionPanel.activeSelf)
        {
            panel.position += Vector3.Cross(Vector3.up, forward) * 1.3f + Vector3.up * 0.25f;
            panel.localScale = Vector3.one * 0.0010f;
        }
        else panel.localScale = Vector3.one * 0.0013f;
        panel.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }
    void ShowConnection(bool value) { connectionPanel.SetActive(value); gameplayPanel.SetActive(!value); PlacePanel(); }
    void LateUpdate()
    {
        if (!Built) return;
        if (XRSettings.isDeviceActive)
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!placedForXR && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked)
            { placedForXR = true; PlacePanel(); }
        }
        UpdateGaze();
        if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + 0.2f; Refresh(); }
    }
    void OnApplicationFocus(bool focused) { if (focused) placedForXR = false; }
    void OnApplicationPause(bool paused) { if (!paused) placedForXR = false; }

    void UpdateGaze()
    {
        if (EventSystem.current == null) return;
        var pointer = new PointerEventData(EventSystem.current) { position = new Vector2(viewCamera.pixelWidth * 0.5f, viewCamera.pixelHeight * 0.5f) };
        hits.Clear(); raycaster.Raycast(pointer, hits);
        UnityEngine.UI.Button target = null;
        foreach (var hit in hits)
        {
            var candidate = hit.gameObject.GetComponentInParent<UnityEngine.UI.Button>();
            if (candidate != null && candidate.IsInteractable()) { target = candidate; break; }
        }
        bool trigger = Pressed(XRNode.RightHand) || Pressed(XRNode.LeftHand);
        if (target != looked)
        {
            if (looked != null) looked.GetComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.23f, 0.37f);
            looked = target; held = 0; lastActivated = null;
        }
        if (target != null && (target != lastActivated || (trigger && !triggerWasDown)))
        {
            held += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(held / dwellSeconds);
            target.GetComponent<UnityEngine.UI.Image>().color = Color.Lerp(new Color(0.12f, 0.23f, 0.37f), new Color(0.08f, 0.55f, 0.72f), progress);
            gazeText.text = "선택 준비 " + Mathf.RoundToInt(progress * 100) + "% — 다른 곳을 보면 취소";
            if (progress >= 1 || (trigger && !triggerWasDown))
            { lastActivated = target; target.onClick.Invoke(); held = 0; gazeText.text = "선택됨. 다시 선택하려면 잠시 다른 곳을 보세요."; }
        }
        else if (target == null) gazeText.text = "흰 점을 버튼에 맞추고 1.5초 기다리세요.";
        triggerWasDown = trigger;
    }
    static bool Pressed(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        return device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger) && trigger;
    }
    public void Refresh()
    {
        if (!Built) return;
        var net = session.Network; var game = session.Game; var mine = game.Board.me; var other = game.Board.opponent;
        if (session.Ready != lastReady) { lastReady = session.Ready; ShowConnection(!session.Ready); }
        string role = net.Connected ? (net.IsHost ? "호스트" : "클라이언트") : "미연결";
        string turn = session.Ready ? (game.IsGameOver ? (mine.IsDefeated ? "패배" : "승리") : (game.State.CurrentPlayer == PlayerSide.Me ? "내 턴" : "상대 턴")) : "연결 대기";
        string order = session.Ready ? (mine.isSecondPlayer ? "후공" : "선공") : "미정";
        statusText.text = $"{net.StatusText}\n내 IP: {net.LocalAddresses}\n상대 IP: {net.PeerAddress} / {role} / 나는 {order} / {turn}\n"
            + $"나: 체력 {mine.HeroHealth} · 코스트 {mine.currentCost}/{VRGameSession.Coin.DisplayCapacity(PlayerSide.Me)}   상대: 체력 {other.HeroHealth} · 코스트 {other.currentCost}/{VRGameSession.Coin.DisplayCapacity(PlayerSide.Opponent)}\n"
            + $"뽑기 확인 대기: 나 {VRGameSession.PendingPhysicalDraws(PlayerSide.Me)} · 상대 {VRGameSession.PendingPhysicalDraws(PlayerSide.Opponent)}\n새 게임 횟수 {session.Starts}";
        fieldsText.text = Field("내 필드", mine) + "\n" + Field("상대 필드", other);
        actionText.text = session.detector != null && !session.detector.CameraReady ? session.detector.CameraStatus : (session.anchor != null && !session.anchor.IsReady ? "중앙 마커 0번을 3.5초 이상 비춰 테이블을 맞추세요." : (session.seating != null && !session.seating.IsReady ? "테이블 한쪽에 서세요. 내 자리를 자동으로 맞추는 중입니다." : session.LastAction));
        foreach (var button in gameButtons) button.interactable = session.CanAct;
        Buttons["Coin"].interactable = session.CanAct && VRGameSession.Coin.CanUse(PlayerSide.Me);
        Buttons["DrawConfirm"].interactable = session.HardwareReady && session.Ready && net.Connected && !game.IsGameOver && VRGameSession.PendingPhysicalDraws(PlayerSide.Me) > 0;
        Buttons["Rematch"].interactable = session.Ready && net.Connected;
    }
    static string Field(string title, PlayerBoardState board)
    {
        var text = new StringBuilder(title + ":  ");
        for (int i = 0; i < 4; i++)
        {
            var c = board.lanes[i]; text.Append(i + 1).Append(':');
            if (c == null) text.Append("빈칸"); else text.Append('#').Append(c.data.cardId).Append(' ').Append(c.currentAttack).Append('/').Append(c.currentHealth);
            text.Append("   ");
        }
        return text.ToString();
    }

    static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>();
    }
    static void Height(GameObject go, float value)
    {
        var rect = go.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(rect.sizeDelta.x, value);
        var e = go.AddComponent<UnityEngine.UI.LayoutElement>(); e.minHeight = value; e.preferredHeight = value; e.flexibleHeight = 0;
    }
    TextMeshProUGUI Label(Transform parent, string name, string text, float size, float height)
    {
        var rect = Rect(parent, name); var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.fontSize = size; label.color = Ink; label.text = text;
        label.raycastTarget = false; label.enableWordWrapping = true;
        label.alignment = TextAlignmentOptions.MidlineLeft; Height(rect.gameObject, height); return label;
    }
    static RectTransform Vertical(Transform parent, string name, float height)
    {
        var rect = Rect(parent, name); Height(rect.gameObject, height);
        var layout = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>(); layout.spacing = 8;
        layout.childControlWidth = true; layout.childControlHeight = false; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        return rect;
    }
    static RectTransform Row(Transform parent, string name, float height)
    {
        var rect = Rect(parent, name); Height(rect.gameObject, height);
        var layout = rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>(); layout.spacing = 8;
        layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = true;
        return rect;
    }
    static RectTransform Grid(Transform parent, string name, float height, int columns, float cellHeight)
    {
        var rect = Rect(parent, name); Height(rect.gameObject, height);
        var layout = rect.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        layout.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = columns;
        layout.cellSize = new Vector2((1056 - (columns - 1) * 8) / columns, cellHeight); layout.spacing = new Vector2(8, 8);
        return rect;
    }
    UnityEngine.UI.Button Button(Transform parent, string key, string text, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(parent, key); Height(rect.gameObject, 60);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(0.12f, 0.23f, 0.37f); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
        var colors = button.colors; colors.disabledColor = new Color(0.25f, 0.27f, 0.30f); button.colors = colors;
        var label = Label(rect, "Label", text, 25, 50); label.alignment = TextAlignmentOptions.Center;
        var textRect = label.rectTransform; textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(8, 3); textRect.offsetMax = new Vector2(-8, -3);
        Buttons.Add(key, button); return button;
    }
}



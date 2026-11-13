using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class ProductionPolishVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Private).Invoke(value, args);
    public static void Capture(ProductionGameUI ui, ProductionGameController flow)
    {
        string[] args = Environment.GetCommandLineArgs(); int key = Array.IndexOf(args, "-polishPreview");
        if (key < 0) return;
        string folder = args[key + 1]; Directory.CreateDirectory(folder);
        var feedback = ui.GetComponent<ProductionTableFeedback>();
        if (feedback == null || feedback.LaneCount != 8 || ui.tableMaterial == null) throw new Exception("Eight presentation lanes/material missing");
        var panel = (RectTransform)typeof(ProductionGameUI).GetField("panel", Private).GetValue(ui);
        var cam = ui.viewCamera;
        cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        cam.orthographic = true; cam.orthographicSize = 0.83f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.015f, 0.025f, 0.045f);
        cam.cullingMask = 1 << 30;
        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        Call(ui, "ShowConnection", true); Draw(ui, panel, cam, Path.Combine(folder, "connection.png"));
        typeof(GameNetworkManager).GetField("connected", Private).SetValue(flow.Network, true);
        flow.detector = null; flow.anchor = null; flow.seating = null;
        flow.Game.StartGame(PlayerSide.Me, dealCards: false); Call(flow, "OnStarted", PlayerSide.Me);
        flow.Game.Board.me.currentCost = 3; flow.Game.Board.me.maxCost = 5;
        flow.Game.Board.me.heroDamage = 4; flow.Game.Board.opponent.heroDamage = 9;
        ui.Refresh(); Call(ui, "ShowConnection", false);
        Draw(ui, panel, cam, Path.Combine(folder, "gameplay.png"));
        if (flow.TryPlacePhysicalCard(16, 0) || !flow.LastAction.Contains("코스트가")) throw new Exception("Specific insufficient-cost feedback missing");
        Call(feedback, "LateUpdate");
        flow.Game.Board.me.lanes[0] = new CardInstance(flow.Database.GetCardById(5), PlayerSide.Me);
        Call(feedback, "LateUpdate");
        bool found = false;
        foreach (var label in feedback.GetComponentsInChildren<TextMeshPro>())
            found |= label.text.Contains("바위골렘") && label.text.Contains("체력 4");
        if (!found) throw new Exception("Live table card stats missing");
        flow.Game.Board.me.lanes[0].currentHealth = 2; Call(feedback, "LateUpdate");
        found = false;
        foreach (var label in feedback.GetComponentsInChildren<TextMeshPro>())
            found |= label.text.Contains("바위골렘") && label.text.Contains("체력 2");
        if (!found) throw new Exception("Table stats failed to update");
        // Isolated table framing for visual QA; all transforms are temporary Play-mode fixture changes.
        panel.gameObject.SetActive(false);
        cam.transform.position = new Vector3(0, 0.75f, -0.85f); cam.transform.LookAt(Vector3.zero); cam.orthographicSize = 0.48f;
        foreach (var slot in UnityEngine.Object.FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None))
            slot.transform.SetPositionAndRotation(new Vector3(-0.30f + slot.lane * 0.20f, 0, -0.20f), Quaternion.identity);
        foreach (var slot in UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None))
            slot.transform.SetPositionAndRotation(new Vector3(-0.30f + slot.lane * 0.20f, 0, 0.20f), Quaternion.Euler(0, 180, 0));
        Call(feedback, "LateUpdate");
        foreach (Transform t in feedback.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        foreach (var label in feedback.GetComponentsInChildren<TextMeshPro>())
        {
            label.ForceMeshUpdate();
            if (label.preferredHeight > 0.10f) throw new Exception("Table label exceeds world-space height");
        }
        SaveCamera(cam, Path.Combine(folder, "table.png"));
        panel.gameObject.SetActive(true); cam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); cam.orthographicSize = 0.83f;
        foreach (Transform t in feedback.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        foreach (Transform t in panel.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
        flow.Game.EndTurn(); ui.Refresh();
        if (ui.Buttons["EndTurn"].interactable) throw new Exception("Opponent turn enabled input");
        flow.Game.Board.opponent.heroDamage = flow.Game.Board.opponent.maxHeroHealth;
        flow.Game.EndTurn(); ui.Refresh();
        Draw(ui, panel, cam, Path.Combine(folder, "result.png"));
        Debug.Log("PRODUCTION_POLISH_PASS: eight lanes, live stats, specific rejection, opponent-turn gate, UI captures");
    }
    static void Draw(ProductionGameUI ui, RectTransform panel, Camera cam, string file)
    {
        panel.SetPositionAndRotation(new Vector3(0, 0, 2), Quaternion.identity); panel.localScale = Vector3.one * 0.0013f;
        Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate(panel); Canvas.ForceUpdateCanvases();
        foreach (Transform child in panel)
        {
            if (!child.gameObject.activeSelf || !(child is RectTransform rect)) continue;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            if (panel.InverseTransformPoint(corners[0]).y < panel.rect.yMin - 2)
                throw new Exception("Panel overflow: " + child.name);
        }
        foreach (var text in panel.GetComponentsInChildren<TextMeshProUGUI>())
        {
            text.ForceMeshUpdate();
            if (text.preferredHeight > text.rectTransform.rect.height + 8) throw new Exception("Text clips: " + text.name);
        }
        SaveCamera(cam, file);
    }
    static void SaveCamera(Camera cam, string file)
    {
        var rt = new RenderTexture(1100, 1180, 24); cam.targetTexture = rt;
        cam.Render(); var before = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); File.WriteAllBytes(file, image.EncodeToPNG());
        RenderTexture.active = before; cam.targetTexture = null;
        UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(rt);
    }
}

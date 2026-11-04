using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ProductionGameVerification
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryW(string path);
    public static void CompleteChecks() { Run(); KJH.Balancing.ModernRuleVerification.CompleteChecks(); }
    public static void Run()
    {
        string nativePath = System.IO.Path.GetFullPath("Library/PackageCache/com.opencvplus.unity@a3402bbe87a5/Runtime/Plugins/Windows/x86_64/OpenCvSharpExtern.dll");
        Check(LoadLibraryW(nativePath) != IntPtr.Zero, "PC OpenCV native load failed: " + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        using (var oldDictionary = OpenCvSharp.Aruco.CvAruco.GetPredefinedDictionary(OpenCvSharp.Aruco.PredefinedDictionaryName.Dict4X4_50))
        using (var fullDictionary = OpenCvSharp.Aruco.CvAruco.GetPredefinedDictionary(OpenCvSharp.Aruco.PredefinedDictionaryName.Dict4X4_100))
        using (var original = oldDictionary.BytesList)
        using (var expanded = fullDictionary.BytesList)
        using (var first50 = expanded.RowRange(0, 50))
        {
            Check(expanded.Rows == 100 && OpenCvSharp.Cv2.Norm(original, first50) == 0, "Original marker compatibility failed");
            Debug.Log("PRODUCTION_MARKER_NATIVE_PASS: IDs 0-99; original first 50 patterns identical");
        }
        ProductionSeatingVerification.Run();
        NetworkRecoveryVerification.Run();
        ProductionGameBuilder.Generate();
        var flow = UnityEngine.Object.FindFirstObjectByType<ProductionGameController>();
        var slots = UnityEngine.Object.FindObjectsByType<ProductionCardSlot>(FindObjectsSortMode.None);
        Check(flow != null && slots.Length == 4, "Production flow or physical slots missing");
        Check(UnityEngine.Object.FindObjectsByType<ProductionOpponentView>(FindObjectsSortMode.None).Length == 4, "Remote models missing");
        Check(UnityEngine.Object.FindObjectsByType<SlotModelSpawner>(FindObjectsSortMode.None).Length == 0, "Legacy input still active");
        Check(UnityEngine.Object.FindFirstObjectByType<MarkerDetecter>() != null && UnityEngine.Object.FindFirstObjectByType<StartInit>() != null,
            "Actual MR camera/marker/anchor pipeline missing");
        Check(UnityEngine.Object.FindFirstObjectByType<TwoHeadsetTestSession>() == null, "Diagnostic session must not be in production");
        flow.detector = null; flow.anchor = null; flow.seating = null; // Hardware startup is verified separately on Quest.
        flow.Initialize(); Call(flow.hud, "Start");
        var net = flow.Network;
        try
        {
            Check(!flow.TryPlacePhysicalCard(1, 0), "Disconnected physical input accepted");
            Set(net, "connected", true);
            Check(!flow.TryPlacePhysicalCard(1, 0), "Input before GAME_START accepted");
            Call(net, "HandleLine", "GAME_START|ME"); Call(net, "Update");
            Check(flow.Ready && flow.Game.Board.me.currentCost == 1, "Production HUD/session failed to start");
            var waitingSeat = flow.gameObject.AddComponent<ProductionSeating>(); flow.seating = waitingSeat;
            Check(!flow.hud.TryUseSpell(24), "Direct HUD spell bypassed hardware readiness");
            Check(!flow.TryPlacePhysicalCard(1, 0), "Physical card bypassed hardware readiness");
            var menu = UnityEngine.Object.FindFirstObjectByType<ProductionGameUI>(); menu.BuildNow(); menu.Refresh();
            var status = (TMPro.TextMeshProUGUI)typeof(ProductionGameUI).GetField("statusText", Private).GetValue(menu);
            Check(status.text.Contains("내 턴"), "Waiting hardware changed the displayed turn");
            flow.seating = null; UnityEngine.Object.DestroyImmediate(waitingSeat);
            foreach (int missing in new[] { 9, 12, 18, 19 })
            {
                var tokenCard = new CardInstance(flow.Database.GetCardById(missing), PlayerSide.Me);
                var token = ProductionCardLabel.Create(tokenCard, flow.transform);
                var view = token.GetComponent<ProductionCardLabel>(); Call(view, "LateUpdate");
                var text = token.GetComponent<TMPro.TextMeshPro>(); text.ForceMeshUpdate();
                Check(text.text.Contains(tokenCard.data.cardName), "Fallback card name missing");
                tokenCard.currentAttack += 2; tokenCard.currentHealth = 1; Call(view, "LateUpdate");
                Check(text.text.EndsWith(tokenCard.currentAttack + " / 1"), "Fallback token stats did not update");
                UnityEngine.Object.DestroyImmediate(token);
            }
            Debug.Log("PRODUCTION_FALLBACK_LABEL_PASS: four missing models have live card/stat labels");
            Debug.Log("PRODUCTION_READINESS_UI_PASS: direct HUD and physical card blocked, actual turn stays visible");
            Check(flow.TryPlacePhysicalCard(1, 0), "Production physical unit rejected");
            Check(flow.Game.Board.me.currentCost == 0 && flow.Game.Board.me.lanes[0].data.cardId == 1, "Physical cost/lane mismatch");
            Check(!flow.TryPlacePhysicalCard(5, 1), "Insufficient mana accepted");
            flow.Game.Board.me.lanes[0] = null; flow.Game.Board.me.currentCost = 8;
            Check(!flow.TryPlacePhysicalCard(1, 0), "Dead card was replayed");
            flow.Game.EndTurn();
            Check(!flow.TryPlacePhysicalCard(5, 1), "Opponent-turn physical input accepted");
            Call(net, "HandleLine", "CARD_PLAYED|5|1"); Call(net, "Update");
            Check(flow.Game.Board.opponent.lanes[1]?.data.cardId == 5, "Actual HUD failed to apply remote unit");
            flow.Game.EndTurn(); flow.Game.Board.me.currentCost = 8;
            Check(flow.TryPlacePhysicalCard(24, 0), "Physical spell rejected");
            Check(!flow.TryPlacePhysicalCard(24, 0), "Physical spell replay accepted");
            int pending = VRGameSession.PendingPhysicalDraws(PlayerSide.Me);
            flow.ConfirmDraw(); Check(VRGameSession.PendingPhysicalDraws(PlayerSide.Me) == pending - 1, "HUD draw confirmation failed");
            Call(flow, "OnDisconnected");
            var turn = flow.Game.State.CurrentPlayer;
            flow.EndTurn(); Check(flow.Game.State.CurrentPlayer == turn, "Disconnected action advanced match");
            Call(net, "HandleLine", "GAME_START|PEER"); Call(net, "Update");
            Check(flow.Ready && flow.Game.Board.me.lanes.All(c => c == null) && flow.Game.Board.opponent.lanes.All(c => c == null), "Reconnect retained previous board");
            Check(flow.Game.Board.me.isSecondPlayer, "Reconnected starter mapping wrong");
            var ui = UnityEngine.Object.FindFirstObjectByType<ProductionGameUI>(); ui.BuildNow();
            Check(!ui.Buttons.ContainsKey("PlaceCard") && !ui.Buttons.ContainsKey("Spell24"), "Production has synthetic card controls");
            Debug.Log("PRODUCTION_GAME_INTEGRATION_SUCCESS: actual MR scene; physical card/mana/turn/replay guards; actual HUD remote input; physical spell/draw; reconnect reset; production controls");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(flow.gameObject);
            UnityEngine.Object.DestroyImmediate(net.gameObject);
            VRGameSession.RequireNetwork = false; VRGameSession.NetworkReady = false;
        }
    }
}






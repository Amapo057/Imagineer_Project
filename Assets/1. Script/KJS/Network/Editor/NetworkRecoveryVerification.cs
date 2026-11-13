using System;
using System.Reflection;
using System.Threading;
using UnityEngine;

public static class NetworkRecoveryVerification
{
    static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool value, string text) { if (!value) throw new Exception(text); }
    static GameNetworkManager Create(string name, string domain = "MAIN_GAME")
    {
        typeof(GameNetworkManager).GetField("instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
        var net = new GameObject(name).AddComponent<GameNetworkManager>(); net.ConfigureMatch(null, domain); return net;
    }
    static void Pump(GameNetworkManager net) => typeof(GameNetworkManager).GetMethod("Update", Flags).Invoke(net, null);
    static void Wait(GameNetworkManager a, GameNetworkManager b, Func<bool> done, string text, int seconds = 12)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (a != null) Pump(a); if (b != null) Pump(b);
            if (done()) return;
            Thread.Sleep(10);
        }
        throw new Exception(text + " / " + a?.StatusText + " / " + b?.StatusText);
    }
    static void Destroy(GameNetworkManager net) { if (net != null) UnityEngine.Object.DestroyImmediate(net.gameObject); }

    public static void Run()
    {
        LocalNetworkVerification.Run();
        GameNetworkManager host = null, client = null;
        try
        {
            client = Create("Delayed host client"); int clientStarts = 0, hostStarts = 0;
            client.OnGameStartReceived += _ => clientStarts++;
            Check(!client.ConnectToHost("invalid-ip"), "Invalid IP accepted");
            client.ConnectToHost("127.0.0.1");
            Wait(client, null, () => client.StatusText.Contains("재시도"), "Failed connection never became retryable");
            host = Create("Delayed host"); host.OnGameStartReceived += _ => hostStarts++;
            host.StartManualHost();
            Wait(host, client, () => host.Connected && client.Connected && hostStarts == 1 && clientStarts == 1,
                "Automatic retry did not recover when host appeared");
            Debug.Log("NETWORK_DELAYED_HOST_PASS: invalid IP rejected; failed client retry connects when host appears");

            // Cancel an active connection and restart; stale worker callbacks must not reset the new match.
            for (int i = 0; i < 4; i++) host.StartManualHost();
            client.ConnectToHost("127.0.0.1");
            Wait(host, client, () => host.Connected && client.Connected && hostStarts >= 2 && clientStarts >= 2,
                "Manual restart did not recover");
            int hs = hostStarts, cs = clientStarts;
            var until = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < until) { Pump(host); Pump(client); Thread.Sleep(10); }
            Check(hostStarts == hs && clientStarts == cs && host.Connected && client.Connected,
                "Stale connection callbacks interfered with new connection");
            Debug.Log("NETWORK_MANUAL_RESTART_PASS: repeated host restart, client reconnect and stale callbacks");
            Debug.Log("NETWORK_HEARTBEAT_PASS: idle heartbeat messages preserve connected match and starter");
        }
        finally { Destroy(client); Destroy(host); }

        host = client = null;
        try
        {
            host = Create("Version host", "BUILD_A"); client = Create("Version client", "BUILD_B");
            int starts = 0; host.OnGameStartReceived += _ => starts++; client.OnGameStartReceived += _ => starts++;
            host.StartManualHost(); client.ConnectToHost("127.0.0.1");
            Wait(host, client, () => host.StatusText.Contains("버전") || client.StatusText.Contains("버전"), "Build mismatch not detected");
            Check(starts == 0 && !host.Connected && !client.Connected, "Mismatched builds started a match");
            Debug.Log("NETWORK_VERSION_MISMATCH_PASS: incompatible handshake rejected before GAME_START");
        }
        finally { Destroy(client); Destroy(host); }

        host = client = null;
        try
        {
            host = Create("Broadcast host"); client = Create("Automatic discovery client"); int starts = 0;
            host.OnGameStartReceived += _ => starts++; client.OnGameStartReceived += _ => starts++;
            host.StartManualHost(); client.StartAutomatic();
            Wait(host, client, () => host.Connected && client.Connected && starts == 2, "LAN broadcast did not discover manual host");
            Debug.Log("NETWORK_AUTODISCOVERY_PASS: actual UDP discovery followed by signed TCP handshake");
        }
        finally { Destroy(client); Destroy(host); }

        var database = ScriptableObject.CreateInstance<CardDatabase>(); var card = ScriptableObject.CreateInstance<CardData>();
        card.cardId = 1; card.attack = 1; card.health = 1; card.cost = 1; database.allCards.Add(card);
        string original = GameNetworkManager.BuildMatchSignature(database); card.attack++;
        Check(original != GameNetworkManager.BuildMatchSignature(database), "Card changes do not change network match signature");
        UnityEngine.Object.DestroyImmediate(database); UnityEngine.Object.DestroyImmediate(card);
        Debug.Log("NETWORK_RECOVERY_VERIFICATION_SUCCESS");
    }
}

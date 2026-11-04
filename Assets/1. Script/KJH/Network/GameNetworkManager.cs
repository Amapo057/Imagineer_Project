using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

// LAN discovery + TCP. All gameplay callbacks execute on Unity's main thread.
public class GameNetworkManager : MonoBehaviour
{
    public enum ConnectionMode { Automatic, Host, Client }
    public const string ProtocolTag = "IMAGINEER_LAN_3";
    const int GamePort = 7777, DiscoveryPort = 7778, ConnectTimeoutMs = 5000;
    private static GameNetworkManager instance;
    public static GameNetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GameNetworkManager (auto)");
                instance = go.AddComponent<GameNetworkManager>();
                if (Application.isPlaying) DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    private volatile bool isHost, connected;
    private volatile bool running = true;
    private int generation;
    private bool started;
    private ConnectionMode mode;
    private IPAddress manualAddress;
    private long retryAtTicks;
    private long nextHeartbeatTicks;
    private volatile string status = "준비 중", peerAddress = "", signature = "";
    private string myToken = Guid.NewGuid().ToString("N");
    private readonly object sessionLock = new object(), sendLock = new object();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private UdpClient discoveryUdp;
    private TcpListener listener;
    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread discoveryThread, sessionThread, receiveThread;

    public bool Connected => connected;
    public bool IsHost => isHost;
    public string StatusText => status;
    public string PeerAddress => peerAddress;
    public string MatchSignature => signature;
    public string LocalAddresses { get; private set; } = "";
    public ConnectionMode Mode => mode;
    public event Action<PlayerSide> OnGameStartReceived;
    public event Action OnEndTurnReceived, OnCoinUsedReceived, OnDrawConfirmedReceived, OnRematchReceived, OnDisconnected;
    public event Action<int, int> OnCardPlayedReceived, OnSpellCastReceived;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        ConfigureMatch(null);
        LocalAddresses = GetLocalAddressText();
    }

    public static string BuildMatchSignature(CardDatabase database, string domain = "MAIN_GAME")
    {
        var text = new StringBuilder(domain).Append('|').Append(Application.buildGUID).Append('|').Append(JsonUtility.ToJson(new MatchRuleSet()))
            .Append('|').Append(GameRules.LaneCount).Append('|').Append(GameRules.DeckSize)
            .Append('|').Append(GameRules.MaxCost).Append('|').Append(GameRules.SecondPlayerStartingCost);
        if (database != null)
            foreach (var c in database.allCards.Where(c => c != null).OrderBy(c => c.cardId))
                text.Append('|').Append(c.cardId).Append(':').Append(c.cost).Append(':').Append(c.attack)
                    .Append(':').Append(c.health).Append(':').Append((int)c.keywords).Append(':').Append((int)c.cardType);
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").Substring(0, 16);
    }

    public void ConfigureMatch(CardDatabase database, string domain = "MAIN_GAME")
    {
        string next = BuildMatchSignature(database, domain);
        bool changed = signature != next;
        signature = next;
        if (started && changed) RetryConnection();
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AcquireMulticastLock();
#endif
        if (!started) StartAutomatic();
    }

    public void StartAutomatic() => Restart(ConnectionMode.Automatic, null);
    public void StartManualHost() => Restart(ConnectionMode.Host, null);
    public bool ConnectToHost(string address)
    {
        if (!IPAddress.TryParse(address?.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast))
        { SetStatus("호스트의 IPv4 주소를 확인하세요."); return false; }
        Restart(ConnectionMode.Client, ip); return true;
    }
    public void RetryConnection() => Restart(mode, manualAddress);

    void Restart(ConnectionMode nextMode, IPAddress address)
    {
        bool wasConnected = connected;
        StopTransport();
        running = true; started = true; mode = nextMode; manualAddress = address; retryAtTicks = 0;
        if (wasConnected) OnDisconnected?.Invoke();
        BeginAttempt();
    }

    void BeginAttempt()
    {
        int id = generation;
        LocalAddresses = GetLocalAddressText();
        if (mode == ConnectionMode.Automatic)
        {
            SetStatus("같은 Wi-Fi에서 상대를 찾는 중… 자동 검색이 안 되면 수동 연결");
            discoveryThread = Background(() => DiscoverAttempt(id));
        }
        else if (mode == ConnectionMode.Host)
        {
            isHost = true; SetStatus("호스트 대기 중 — 다른 기기에서 이 기기의 IP로 연결");
            sessionThread = Background(() => RunHostAttempt(id));
            discoveryThread = Background(() => AdvertiseHost(id));
        }
        else if (manualAddress != null)
        {
            isHost = false; SetStatus($"{manualAddress}에 연결 중…");
            sessionThread = Background(() => RunClientAttempt(manualAddress, id));
        }
    }

    static Thread Background(ThreadStart action) { var thread = new Thread(action) { IsBackground = true }; thread.Start(); return thread; }
    bool Current(int id) => running && id == Volatile.Read(ref generation);
    void Queue(int id, Action action) => mainThreadActions.Enqueue(() => { if (Current(id)) action(); });
    void SetStatus(string value) { status = value; Debug.Log("[Net] " + value); }
    void Update()
    {
        while (mainThreadActions.TryDequeue(out var action)) action.Invoke();
        if (running && retryAtTicks != 0 && DateTime.UtcNow.Ticks >= retryAtTicks)
        { retryAtTicks = 0; BeginAttempt(); }
        if (connected && DateTime.UtcNow.Ticks >= nextHeartbeatTicks)
        { nextHeartbeatTicks = DateTime.UtcNow.AddSeconds(2).Ticks; SendRaw("PING"); }
    }

    void StopTransport()
    {
        Interlocked.Increment(ref generation);
        lock (sessionLock)
        {
            connected = false; peerAddress = "";
            try { discoveryUdp?.Close(); } catch { }
            try { listener?.Stop(); } catch { }
            try { stream?.Close(); } catch { }
            try { tcpClient?.Close(); } catch { }
            discoveryUdp = null; listener = null; stream = null; tcpClient = null;
        }
    }

    void Failed(int id, string reason)
    {
        Queue(id, () =>
        {
            bool hadConnection = connected;
            StopTransport();
            SetStatus(reason + " — 2초 후 재시도 / 재시도 버튼으로 즉시 다시 연결");
            if (hadConnection) OnDisconnected?.Invoke();
            retryAtTicks = DateTime.UtcNow.AddSeconds(2).Ticks;
        });
    }

    void OnDestroy()
    {
        running = false; StopTransport();
#if UNITY_ANDROID && !UNITY_EDITOR
        ReleaseMulticastLock();
#endif
        if (instance == this) instance = null;
    }

    // Retain these entry points for the existing loopback regression harness.
    private void DiscoverLoop() => DiscoverAttempt(generation);
    private void RunAsHost() => RunHostAttempt(generation);
    private void RunAsClient(IPAddress address) => RunClientAttempt(address, generation);

    void DiscoverAttempt(int id)
    {
        UdpClient udp = null;
        try
        {
            udp = new UdpClient(); udp.EnableBroadcast = true;
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort)); udp.Client.ReceiveTimeout = 300;
            lock (sessionLock) { if (!Current(id)) return; discoveryUdp = udp; }
            var targets = GetBroadcastAddresses(); var endpoint = new IPEndPoint(IPAddress.Any, 0);
            var lastSend = DateTime.MinValue; bool launched = false, mismatchShown = false;
            while (Current(id) && !connected)
            {
                if ((DateTime.UtcNow - lastSend).TotalSeconds >= 1)
                {
                    string role = launched ? NetMsg.Host : NetMsg.Seeking;
                    byte[] bytes = Encoding.UTF8.GetBytes($"{role}|{myToken}|{ProtocolTag}|{signature}");
                    foreach (var target in targets) try { udp.Send(bytes, bytes.Length, new IPEndPoint(target, DiscoveryPort)); } catch { }
                    lastSend = DateTime.UtcNow;
                }
                byte[] received;
                try { received = udp.Receive(ref endpoint); } catch (SocketException) { continue; }
                if (!Current(id)) break;
                var parts = Encoding.UTF8.GetString(received).Split('|');
                if (parts.Length < 2 || parts[1] == myToken) continue;
                if (parts[0] != NetMsg.Seeking && parts[0] != NetMsg.Host) continue;
                if (parts.Length != 4 || parts[2] != ProtocolTag || parts[3] != signature)
                {
                    if (!mismatchShown) { Queue(id, () => SetStatus("상대와 APK·카드·규칙 버전이 다릅니다. 같은 APK를 두 기기에 설치하세요.")); mismatchShown = true; }
                    continue;
                }
                if (launched) continue;
                bool asHost = parts[0] != NetMsg.Host && string.CompareOrdinal(myToken, parts[1]) > 0;
                lock (sessionLock) { if (!Current(id)) break; isHost = asHost; }
                launched = true; var address = endpoint.Address;
                Queue(id, () => SetStatus(asHost ? "상대 발견 — 호스트 시작" : $"상대 발견 — {address}에 연결 중"));
                sessionThread = Background(() => { if (asHost) RunHostAttempt(id); else RunClientAttempt(address, id); });
                if (!asHost) break;
            }
        }
        catch (Exception e) { if (Current(id)) Failed(id, "자동 검색 실패: " + e.Message); }
        finally { try { udp?.Close(); } catch { } }
    }

    void AdvertiseHost(int id)
    {
        // A manually selected host can also be found by a newly launched automatic client.
        try
        {
            using var udp = new UdpClient(); udp.EnableBroadcast = true;
            var targets = GetBroadcastAddresses();
            while (Current(id) && !connected)
            {
                byte[] bytes = Encoding.UTF8.GetBytes($"{NetMsg.Host}|{myToken}|{ProtocolTag}|{signature}");
                foreach (var address in targets) try { udp.Send(bytes, bytes.Length, new IPEndPoint(address, DiscoveryPort)); } catch { }
                for (int i = 0; i < 10 && Current(id) && !connected; i++) Thread.Sleep(100);
            }
        }
        catch (Exception e) { if (Current(id)) Debug.LogWarning("[Net] 수동 호스트 자동 알림 실패: " + e.Message); }
    }

    void RunHostAttempt(int id)
    {
        if (!Current(id)) return;
        TcpListener server = null;
        try
        {
            server = new TcpListener(IPAddress.Any, GamePort);
            server.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            lock (sessionLock) { if (!Current(id)) return; isHost = true; listener = server; server.Start(); }
            while (Current(id) && !server.Pending()) Thread.Sleep(50);
            if (!Current(id)) return;
            var client = server.AcceptTcpClient(); server.Stop();
            AcceptConnection(client, id, true);
        }
        catch (Exception e) { if (Current(id)) Failed(id, "호스트 시작/연결 실패: " + e.Message); }
        finally { try { server?.Stop(); } catch { } }
    }

    void RunClientAttempt(IPAddress address, int id)
    {
        if (!Current(id)) return;
        TcpClient client = null;
        try
        {
            client = new TcpClient();
            lock (sessionLock) { if (!Current(id)) { client.Close(); return; } isHost = false; tcpClient = client; }
            var pending = client.BeginConnect(address, GamePort, null, null);
            var deadline = DateTime.UtcNow.AddMilliseconds(ConnectTimeoutMs);
            using (var wait = pending.AsyncWaitHandle)
            {
                while (Current(id) && !wait.WaitOne(100))
                    if (DateTime.UtcNow >= deadline) throw new TimeoutException("호스트 연결 시간 초과. IP·같은 Wi-Fi·호스트 대기를 확인하세요.");
            }
            if (!Current(id)) { client.Close(); return; }
            client.EndConnect(pending); AcceptConnection(client, id, false);
        }
        catch (Exception e)
        {
            try { client?.Close(); } catch { }
            if (Current(id)) Failed(id, "연결 실패: " + e.Message);
        }
    }

    void AcceptConnection(TcpClient client, int id, bool host)
    {
        if (!Current(id)) { client.Close(); return; }
        client.NoDelay = true; var connection = client.GetStream();
        lock (sessionLock) { if (!Current(id)) { client.Close(); return; } tcpClient = client; stream = connection; }
        string hello = $"HELLO|{ProtocolTag}|{signature}";
        connection.ReadTimeout = ConnectTimeoutMs; connection.WriteTimeout = ConnectTimeoutMs;
        var bytes = Encoding.UTF8.GetBytes(hello + "\n"); connection.Write(bytes, 0, bytes.Length);
        var received = new StringBuilder();
        while (received.Length < 256)
        {
            int value = connection.ReadByte(); if (value < 0) throw new SocketException();
            if (value == '\n') break; received.Append((char)value);
        }
        if (received.ToString().Trim() != hello) throw new InvalidOperationException("APK·카드·규칙 버전이 다릅니다. 같은 APK를 두 기기에 설치하세요.");
        connection.ReadTimeout = 15000; // Heartbeats detect a lost Wi-Fi peer even without game actions.
        lock (sessionLock)
        {
            if (!Current(id)) { client.Close(); return; }
            connected = true; isHost = host; peerAddress = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
        }
        Queue(id, () => SetStatus(host ? "연결 완료 — 호스트" : "연결 완료 — 클라이언트"));
        if (host) ReflipForEpoch(id);
        receiveThread = Background(() => ReceiveAttempt(connection, id));
    }

    void ReflipFirstPlayerAndBroadcast()
    {
        ReflipForEpoch(generation);
    }
    void ReflipForEpoch(int id)
    {
        lock (sessionLock)
        {
            if (!Current(id) || !connected || !isHost) return;
            bool hostFirst = new System.Random().Next(2) == 0;
            if (SendRaw($"{NetMsg.GameStart}|{(hostFirst ? "PEER" : "ME")}"))
                Queue(id, () => OnGameStartReceived?.Invoke(hostFirst ? PlayerSide.Me : PlayerSide.Opponent));
        }
    }

    void ReceiveAttempt(NetworkStream connection, int id)
    {
        var buffer = new byte[4096]; var pending = new StringBuilder();
        try
        {
            while (Current(id))
            {
                int read = connection.Read(buffer, 0, buffer.Length); if (read <= 0) break;
                pending.Append(Encoding.UTF8.GetString(buffer, 0, read));
                if (pending.Length > 65536) throw new InvalidOperationException("메시지 길이 초과");
                string combined = pending.ToString(); int last = combined.LastIndexOf('\n');
                if (last < 0) continue;
                pending.Clear(); pending.Append(combined.Substring(last + 1));
                foreach (var line in combined.Substring(0, last).Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line)) DispatchLine(line.Trim(), id);
            }
        }
        catch (Exception) { /* Closing the transport intentionally interrupts Read. */ }
        if (Current(id)) Failed(id, "연결 끊김 — 다시 연결하면 새 게임으로 시작합니다");
    }

    private void HandleLine(string line) => DispatchLine(line, generation);
    void DispatchLine(string line, int id)
    {
        var parts = line.Split('|');
        switch (parts[0])
        {
            case "PING": if (Current(id)) SendRaw("PONG"); break;
            case "PONG": break;
            case NetMsg.GameStart:
                if (parts.Length == 2 && (parts[1] == "ME" || parts[1] == "PEER"))
                    Queue(id, () => OnGameStartReceived?.Invoke(parts[1] == "ME" ? PlayerSide.Me : PlayerSide.Opponent));
                break;
            case NetMsg.EndTurn: Queue(id, () => OnEndTurnReceived?.Invoke()); break;
            case NetMsg.CoinUsed: Queue(id, () => OnCoinUsedReceived?.Invoke()); break;
            case NetMsg.DrawConfirmed: Queue(id, () => OnDrawConfirmedReceived?.Invoke()); break;
            case NetMsg.SpellCast:
                if (parts.Length == 3 && int.TryParse(parts[1], out int spell) && int.TryParse(parts[2], out int target) && target >= -1 && target < 4)
                    Queue(id, () => OnSpellCastReceived?.Invoke(spell, target));
                break;
            case NetMsg.CardPlayed:
                if (parts.Length == 3 && int.TryParse(parts[1], out int card) && int.TryParse(parts[2], out int lane) && lane >= 0 && lane < 4)
                    Queue(id, () => OnCardPlayedReceived?.Invoke(card, lane));
                break;
            case NetMsg.Rematch:
                Queue(id, () => OnRematchReceived?.Invoke());
                if (isHost) ReflipForEpoch(id);
                break;
        }
    }

    bool SendRaw(string message)
    {
        int id = generation; var connection = stream;
        if (!connected || connection == null) return false;
        lock (sendLock)
        {
            try { var bytes = Encoding.UTF8.GetBytes(message + "\n"); connection.Write(bytes, 0, bytes.Length); return true; }
            catch (Exception e) { Failed(id, "전송 실패: " + e.Message); return false; }
        }
    }

    public bool TrySendEndTurn(PlayerSide localSide, PlayerSide currentTurn) => localSide == currentTurn && SendRaw(NetMsg.EndTurn);
    public void SendCardPlayed(int cardId, int lane) => SendRaw($"{NetMsg.CardPlayed}|{cardId}|{lane}");
    public void SendCoinUsed() => SendRaw(NetMsg.CoinUsed);
    public void SendSpellCast(int cardId, int lane) => SendRaw($"{NetMsg.SpellCast}|{cardId}|{lane}");
    public void SendDrawConfirmed() => SendRaw(NetMsg.DrawConfirmed);
    public void SendRematch() { if (SendRaw(NetMsg.Rematch) && isHost) ReflipFirstPlayerAndBroadcast(); }

    static IEnumerable<UnicastIPAddressInformation> GetIPv4Interfaces()
    {
        var result = new List<UnicastIPAddressInformation>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().OrderByDescending(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 || n.Name.StartsWith("wlan", StringComparison.OrdinalIgnoreCase)))
                if (nic.OperationalStatus == OperationalStatus.Up)
                    foreach (var item in nic.GetIPProperties().UnicastAddresses)
                        if (item.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(item.Address)) result.Add(item);
        }
        catch (Exception) { }
        return result;
    }
    static string GetLocalAddressText()
    {
        var addresses = GetIPv4Interfaces().Select(a => a.Address.ToString()).Distinct().ToArray();
        if (addresses.Length > 0) return string.Join(" / ", addresses);
        // Some Android runtimes do not expose NetworkInterface data. UDP Connect only
        // selects a local route; no packet is sent and no external server is contacted.
        try
        {
            using var route = new UdpClient(); route.EnableBroadcast = true; route.Connect(IPAddress.Broadcast, DiscoveryPort);
            var address = ((IPEndPoint)route.Client.LocalEndPoint).Address;
            if (!address.Equals(IPAddress.Any) && !IPAddress.IsLoopback(address)) return address.ToString();
        }
        catch (Exception) { }
        return "Wi-Fi 설정에서 IPv4 주소를 확인하세요";
    }
    private List<IPAddress> GetBroadcastAddresses()
    {
        var result = new List<IPAddress>();
        foreach (var item in GetIPv4Interfaces())
            try
            {
                var ip = item.Address.GetAddressBytes(); var mask = item.IPv4Mask?.GetAddressBytes();
                if (mask == null) continue;
                var broadcast = new byte[4]; for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                result.Add(new IPAddress(broadcast));
            }
            catch (Exception) { }
        result.Add(IPAddress.Broadcast); return result.Distinct().ToList();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject multicastLock;
    void AcquireMulticastLock()
    {
        try
        {
            using var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unity.GetStatic<AndroidJavaObject>("currentActivity");
            using var wifi = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            multicastLock = wifi.Call<AndroidJavaObject>("createMulticastLock", "imagineer_net_lock");
            multicastLock.Call("setReferenceCounted", false); multicastLock.Call("acquire");
        }
        catch (Exception e) { Debug.LogWarning("[Net] Wi-Fi 검색 잠금 실패: " + e.Message); }
    }
    void ReleaseMulticastLock() { try { multicastLock?.Call("release"); multicastLock?.Dispose(); } catch { } multicastLock = null; }
#endif
}

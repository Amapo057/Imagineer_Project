using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 로컬 네트워크(같은 와이파이) 안에서 두 헤드셋이 서로를 찾아 TCP로 붙는 매니저.
/// 씬에 직접 안 올려도 됨 - Instance에 처음 접근하는 순간 자동으로 만들어짐(VRGameSession과 같은 패턴).
///
/// 흐름: UDP 브로드캐스트로 서로를 찾음(DiscoverLoop) -> 토큰(GUID) 비교로 누가 호스트/클라이언트인지
/// 정함 -> TCP로 붙음 -> 이후 EndTurn/카드 플레이 같은 작은 텍스트 메시지를 주고받음(lockstep).
///
/// 여러 번 검토하면서 고친 것들:
///   - 늦게 들어온 쪽이 이미 있는 호스트랑 또 토큰 경쟁하지 않도록 HOST 상태를 계속 브로드캐스트함
///   - End Turn은 내 턴일 때만 보내도록 가드(TrySendEndTurn)
///   - listener/stream/tcpClient를 전부 필드로 들고 있어서 OnDestroy에서 확실히 닫음
///     (안 그러면 블로킹 중인 Accept/Read가 안 풀려서 백그라운드 스레드가 Editor Play 세션 넘어서도 살아있을 수 있음)
///   - 연결 시도에 타임아웃을 둠(BeginConnect + WaitOne)
///   - 선공/후공 결정에 UnityEngine.Random 대신 System.Random을 씀
///     (UnityEngine.Random은 메인 스레드 전용이라 백그라운드 스레드에서 부르면 위험함)
///   - 멀티 네트워크 어댑터(PC에 Meta/Oculus 가상 어댑터가 같이 있는 경우) 대비해서
///     모든 활성 IPv4 어댑터의 서브넷 브로드캐스트 주소로 각각 보냄
///   - Connected/IsHost를 volatile로 선언(백그라운드 스레드 쓰기 / 메인 스레드 Update 폴링 간 가시성)
///   - SendRaw에 락을 걸어서 핸드셰이크(백그라운드 스레드)랑 게임 중 전송(메인 스레드)이 겹쳐도
///     스트림에 쓰는 바이트가 안 섞이게 함
///
/// 재접속/재대결(SendRematch/OnDisconnected/ReflipFirstPlayerAndBroadcast)과 안드로이드
/// MulticastLock을 추가함(2026-10-28) - 다만 둘 다 실제 기기 2대로 검증된 적은 없는 코드임:
///   - 재접속: 연결이 끊기면 자동으로 디스커버리를 다시 돌려서 재연결을 시도하고 OnDisconnected로
///     알림. 단, 끊기기 전까지의 보드 상태(라인/명치 등)를 다시 맞추는 로직까지는 없음 - 재접속 후
///     받는 GAME_START는 선공/코스트 공식만 재조정함
///   - 재대결: SendRematch()로 양쪽에 REMATCH를 알리고, 호스트가 다시 선공을 추첨해서 GAME_START로
///     공지함(최초 연결 때와 같은 흐름 재사용). 로컬 보드 리셋은 VRGameSession.ResetGame() 참고
///   - 안드로이드/퀘스트 포팅 시 필요한 WifiManager.MulticastLock은 UNITY_ANDROID 블록으로 추가했고
///     매니페스트에도 권한을 넣어뒀지만, 실제 퀘스트 기기로 디스커버리가 되는지는 아직 미확인
/// </summary>
public class GameNetworkManager : MonoBehaviour
{
    private static GameNetworkManager instance;
    public static GameNetworkManager Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GameNetworkManager (auto)");
                instance = go.AddComponent<GameNetworkManager>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

    private const int GamePort = 7777;
    private const int DiscoveryPort = 7778;
    private const int ConnectTimeoutMs = 5000;

    private volatile bool isHost;
    private volatile bool connected;
    public bool IsHost => isHost;
    public bool Connected => connected;

    public event Action<PlayerSide> OnGameStartReceived;
    public event Action OnEndTurnReceived;
    public event Action OnCoinUsedReceived;
    public event Action<int, int> OnCardPlayedReceived;
    public event Action OnDisconnected;     // 의도치 않게 연결이 끊어졌을 때(OnDestroy로 인한 정상 종료 제외)
    public event Action OnRematchReceived;  // 재대결 요청이 왔을 때(내가 보낸 경우 포함 - 로컬 UI 리셋용)

    private readonly string myToken = Guid.NewGuid().ToString();
    private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();
    private readonly object sendLock = new object();

    private UdpClient discoveryUdp;
    private TcpListener listener;
    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread discoveryThread;
    private Thread sessionThread;
    private Thread receiveThread;

    private volatile bool running = true;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AcquireMulticastLock();
#endif
        discoveryThread = new Thread(DiscoverLoop) { IsBackground = true };
        discoveryThread.Start();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // 안드로이드/퀘스트에서는 Wi-Fi 칩이 절전 상태일 때 브로드캐스트/멀티캐스트 패킷을 그냥
    // 씹어버릴 수 있어서, 디스커버리가 시작되는 동안은 MulticastLock을 들고 있어야 함(PC 두 대로
    // 테스트할 땐 필요 없어서 지금까지 없었던 부분 - 매니페스트에도 CHANGE_WIFI_MULTICAST_STATE
    // 권한을 같이 추가해둬야 createMulticastLock() 자체가 실패하지 않음. 실제 퀘스트 기기로
    // 검증된 적은 없어서, 포팅 테스트할 때 이 쪽부터 확인해보면 됨)
    private AndroidJavaObject multicastLock;

    private void AcquireMulticastLock()
    {
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
            multicastLock = wifiManager.Call<AndroidJavaObject>("createMulticastLock", "imagineer_net_lock");
            multicastLock.Call("setReferenceCounted", true);
            multicastLock.Call("acquire");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Net] MulticastLock 획득 실패(디스커버리가 일부 기기에서 안 될 수 있음): {e.Message}");
        }
    }

    private void ReleaseMulticastLock()
    {
        try { multicastLock?.Call("release"); } catch { }
        multicastLock = null;
    }
#endif

    void Update()
    {
        while (mainThreadActions.TryDequeue(out var action)) action.Invoke();
    }

    void OnDestroy()
    {
        running = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        ReleaseMulticastLock();
#endif

        try { discoveryUdp?.Close(); } catch { }
        try { listener?.Stop(); } catch { }
        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }

        // 위 Close/Stop들이 블로킹 중인 Receive/Accept/Read를 예외로 깨워서
        // 각 스레드가 스스로 빠져나오게 함(Thread.Abort는 .NET에서 위험하고 지원 안 돼서 안 씀).
        // Editor에서 "Reload Domain"을 꺼둔 경우 이게 특히 중요함 - Play를 멈춰도 도메인이
        // 리로드되지 않아서, 소켓을 안 닫으면 이전 세션의 스레드/포트가 다음 Play까지 살아있을 수 있음.
    }

    // ---------------- Discovery ----------------

    private void DiscoverLoop()
    {
        try
        {
            discoveryUdp = new UdpClient();
            discoveryUdp.EnableBroadcast = true;
            discoveryUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            discoveryUdp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            discoveryUdp.Client.ReceiveTimeout = 500; // running 체크 주기
        }
        catch (Exception e)
        {
            Debug.LogError($"[Net] 디스커버리 소켓 바인드 실패: {e.Message}");
            return;
        }

        var broadcastTargets = GetBroadcastAddresses();
        var lastBroadcast = DateTime.MinValue;
        var remoteEP = new IPEndPoint(IPAddress.Any, 0);
        bool sessionStarted = false;

        while (running)
        {
            if ((DateTime.UtcNow - lastBroadcast).TotalMilliseconds > 1000)
            {
                // 세션 시작 전엔 SEEKING, 호스트로 정해진 후엔 계속 HOST로 알림
                // (클라이언트는 연결되면 더 브로드캐스트할 필요 없음 - 아래에서 루프 탈출)
                string myRole = !sessionStarted ? NetMsg.Seeking : NetMsg.Host;
                var payload = Encoding.UTF8.GetBytes($"{myRole}|{myToken}");
                foreach (var addr in broadcastTargets)
                {
                    try { discoveryUdp.Send(payload, payload.Length, new IPEndPoint(addr, DiscoveryPort)); }
                    catch { /* 어댑터 하나가 막혀있어도 나머지로 계속 시도 */ }
                }
                lastBroadcast = DateTime.UtcNow;
            }

            byte[] data;
            try { data = discoveryUdp.Receive(ref remoteEP); }
            catch (SocketException) { continue; } // 타임아웃 - running 다시 체크
            catch (ObjectDisposedException) { break; } // OnDestroy에서 Close()함

            if (sessionStarted && isHost) continue; // 호스트는 계속 듣기만 함(재접속 대비), 새 세션은 또 안 만듦
            if (sessionStarted) break; // 클라이언트는 연결 후엔 디스커버리 끝

            var parts = Encoding.UTF8.GetString(data).Split('|');
            if (parts.Length != 2) continue;
            string peerRole = parts[0];
            string peerToken = parts[1];
            IPAddress peerIp = remoteEP.Address;

            // 브로드캐스트는 같은 PC/같은 네트워크 인터페이스를 통해 보낸 사람 자신에게도
            // 되돌아올 수 있음(윈도우에서 흔함) - 그걸 상대로 착각해서 스스로를 HOST/CLIENT로
            // 잘못 결정하는 걸 막음. 실제로 혼자 Play 했을 때 이 자기수신 때문에 "호스트 연결
            // 타임아웃" 경고가 뜨는 걸 확인해서 추가한 가드임
            if (peerToken == myToken) continue;

            if (peerRole == NetMsg.Host)
            {
                // 이미 호스트가 있으면 토큰 비교 없이 바로 클라이언트로 붙음
                // (늦게 들어온 쪽이 재경쟁하지 않게 하는 게 이 수정의 핵심)
                sessionStarted = true;
                isHost = false;
                sessionThread = new Thread(() => RunAsClient(peerIp)) { IsBackground = true };
                sessionThread.Start();
                continue;
            }

            if (peerRole == NetMsg.Seeking)
            {
                bool iAmHost = string.CompareOrdinal(myToken, peerToken) > 0;
                sessionStarted = true;
                isHost = iAmHost;
                if (iAmHost)
                    sessionThread = new Thread(RunAsHost) { IsBackground = true };
                else
                    sessionThread = new Thread(() => RunAsClient(peerIp)) { IsBackground = true };
                sessionThread.Start();
            }
        }

        try { discoveryUdp?.Close(); } catch { }
    }

    private List<IPAddress> GetBroadcastAddresses()
    {
        // IPAddress.Broadcast(255.255.255.255) 하나만 쓰면 멀티 어댑터 PC(Meta/Oculus 가상 어댑터 등)에서
        // 실제 LAN 어댑터로 안 나갈 수 있어서, 활성화된 모든 IPv4 어댑터의 서브넷 브로드캐스트 주소로 각각 보냄
        var result = new List<IPAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                try
                {
                    if (ua.IPv4Mask == null) continue;
                    byte[] ip = ua.Address.GetAddressBytes();
                    byte[] mask = ua.IPv4Mask.GetAddressBytes();
                    byte[] bcast = new byte[4];
                    for (int i = 0; i < 4; i++) bcast[i] = (byte)(ip[i] | (~mask[i]));
                    result.Add(new IPAddress(bcast));
                }
                catch (NotImplementedException)
                {
                    // 일부 플랫폼(안드로이드 등)에서는 IPv4Mask 조회가 지원 안 될 수 있음 - 그 어댑터는 건너뜀
                }
            }
        }
        if (result.Count == 0) result.Add(IPAddress.Broadcast);
        return result;
    }

    // ---------------- Host / Client ----------------

    private void RunAsHost()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, GamePort);
            listener.Start();
        }
        catch (Exception e)
        {
            Debug.LogError($"[Net] 리스너 시작 실패: {e.Message}");
            return;
        }

        TcpClient client;
        try { client = listener.AcceptTcpClient(); }
        catch (SocketException)
        {
            return; // OnDestroy의 listener.Stop()으로 빠져나온 정상 종료
        }
        finally
        {
            try { listener.Stop(); } catch { }
            listener = null;
        }

        tcpClient = client;
        stream = tcpClient.GetStream();
        connected = true;
        Debug.Log("[Net] 클라이언트 연결됨 - 내가 호스트");

        ReflipFirstPlayerAndBroadcast();

        StartReceiveLoop();
    }

    // 선공/후공을 새로 추첨해서 양쪽에 GAME_START로 알림. 최초 연결(RunAsHost)과 재대결
    // (SendRematch/REMATCH 수신) 둘 다 똑같이 이 메서드를 씀 - 호스트만 호출할 수 있음
    private void ReflipFirstPlayerAndBroadcast()
    {
        // UnityEngine.Random은 메인 스레드 전용이라 백그라운드 스레드에서는 못 씀 - 순수 .NET Random 사용
        bool hostGoesFirst = new System.Random().NextDouble() < 0.5;
        SendRaw($"{NetMsg.GameStart}|{(hostGoesFirst ? "PEER" : "ME")}");

        var mySide = hostGoesFirst ? PlayerSide.Me : PlayerSide.Opponent;
        mainThreadActions.Enqueue(() => OnGameStartReceived?.Invoke(mySide));
    }

    private void RunAsClient(IPAddress hostIp)
    {
        tcpClient = new TcpClient();

        IAsyncResult result;
        try { result = tcpClient.BeginConnect(hostIp, GamePort, null, null); }
        catch (Exception e)
        {
            Debug.LogError($"[Net] 연결 시도 실패: {e.Message}");
            return;
        }

        bool signaled = result.AsyncWaitHandle.WaitOne(ConnectTimeoutMs);
        if (!signaled || !tcpClient.Connected)
        {
            Debug.LogWarning("[Net] 호스트 연결 타임아웃 - 재시도 로직은 추후 추가");
            try { tcpClient.Close(); } catch { }
            return;
        }
        tcpClient.EndConnect(result);

        stream = tcpClient.GetStream();
        connected = true;
        Debug.Log("[Net] 호스트에 연결됨 - 내가 클라이언트");
        StartReceiveLoop();
    }

    private void StartReceiveLoop()
    {
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    private void ReceiveLoop()
    {
        var buffer = new byte[4096];

        // 이전 Read()에서 받았지만 아직 줄바꿈('\n')을 못 받은 조각을 들고 있는 버퍼.
        // TCP는 스트림이라 한 메시지가 두 번의 Read() 호출에 걸쳐 반으로 쪼개져서 올 수 있는데
        // (드물지만 실제로 가능함), 그럴 경우 예전 코드처럼 매 Read() 결과를 그 자리에서 바로
        // '\n' 기준으로 쪼개버리면 쪼개진 조각이 그대로 HandleLine에 들어가서 메시지가 조용히
        // 소실될 수 있었음. 그래서 완전한 한 줄(끝에 '\n'이 붙은 부분)이 모일 때까지 기다렸다가
        // 처리하고, 아직 '\n'이 안 붙은 꼬리 조각은 pending에 남겨뒀다가 다음 Read()와 이어붙임
        var pending = new StringBuilder();

        while (running)
        {
            int read;
            try { read = stream.Read(buffer, 0, buffer.Length); }
            catch (Exception) { break; } // OnDestroy에서 stream.Close()하면 여기로 빠져나옴

            if (read <= 0) break;

            pending.Append(Encoding.UTF8.GetString(buffer, 0, read));

            string combined = pending.ToString();
            int lastNewline = combined.LastIndexOf('\n');
            if (lastNewline < 0) continue; // 아직 완성된 줄이 하나도 없음 - 다음 Read()를 더 기다림

            string completeLines = combined.Substring(0, lastNewline);
            pending.Clear();
            pending.Append(combined.Substring(lastNewline + 1)); // '\n' 뒤 꼬리 조각은 다음 루프로 넘김

            foreach (var line in completeLines.Split('\n'))
            {
                if (!string.IsNullOrEmpty(line)) HandleLine(line.Trim());
            }
        }
        connected = false;

        // running이 여전히 true라는 건 OnDestroy(stream.Close())로 인한 의도된 종료가 아니라
        // 실제로 상대와의 연결이 끊어졌다는 뜻 - 알리고 디스커버리부터 다시 돌려서 재접속을 시도함.
        // (주의: 재접속만 되고, 끊기기 전까지 진행된 보드 상태(라인/명치 등)를 다시 맞추는 로직은
        // 없음 - 재접속 후 받는 새 GAME_START는 선공/코스트 공식만 재조정함. 완전한 상태 동기화는
        // 추후 과제로 남겨둠)
        if (running) RestartDiscoveryForReconnect();
    }

    private void RestartDiscoveryForReconnect()
    {
        mainThreadActions.Enqueue(() => OnDisconnected?.Invoke());

        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }
        stream = null;
        tcpClient = null;

        discoveryThread = new Thread(DiscoverLoop) { IsBackground = true };
        discoveryThread.Start();
    }

    private void HandleLine(string line)
    {
        var parts = line.Split('|');
        switch (parts[0])
        {
            case NetMsg.GameStart:
                var mySide = parts[1] == "ME" ? PlayerSide.Me : PlayerSide.Opponent;
                mainThreadActions.Enqueue(() => OnGameStartReceived?.Invoke(mySide));
                break;
            case NetMsg.EndTurn:
                mainThreadActions.Enqueue(() => OnEndTurnReceived?.Invoke());
                break;
            case NetMsg.CoinUsed:
                mainThreadActions.Enqueue(() => OnCoinUsedReceived?.Invoke());
                break;
            case NetMsg.CardPlayed:
                if (parts.Length >= 3 && int.TryParse(parts[1], out int cardId) && int.TryParse(parts[2], out int laneIdx))
                    mainThreadActions.Enqueue(() => OnCardPlayedReceived?.Invoke(cardId, laneIdx));
                break;
            case NetMsg.Rematch:
                // 상대가 재대결 버튼을 눌러서 보낸 메시지. 호스트인 내가 새로 선공을 추첨해서
                // GAME_START로 다시 알려줘야 함(클라이언트는 그 메시지를 받을 때까지 기다리기만 함)
                mainThreadActions.Enqueue(() => OnRematchReceived?.Invoke());
                // Reset the old round before applying the newly selected starter.
                // Reversing these callbacks overwrites GAME_START with the solo default.
                if (isHost) ReflipFirstPlayerAndBroadcast();
                break;
        }
    }

    private void SendRaw(string msg)
    {
        if (stream == null) return;
        lock (sendLock)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(msg + "\n");
                stream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Net] 전송 실패: {e.Message}");
            }
        }
    }

    // ---------------- 외부에서 호출하는 API ----------------

    // 턴 소유권 가드 - 내 턴이 아니면 네트워크로도 로컬로도 턴을 안 넘김
    public bool TrySendEndTurn(PlayerSide localSide, PlayerSide currentTurn)
    {
        if (currentTurn != localSide)
        {
            Debug.LogWarning("[Net] 내 턴이 아닐 때 EndTurn 시도 - 무시함");
            return false;
        }
        SendRaw(NetMsg.EndTurn);
        return true;
    }

    public void SendCardPlayed(int cardId, int laneIdx)
    {
        SendRaw($"{NetMsg.CardPlayed}|{cardId}|{laneIdx}");
    }

    public void SendCoinUsed() => SendRaw(NetMsg.CoinUsed);

    // 재대결 버튼에서 호출. 상대에게 REMATCH를 알리고, 내가 호스트면 그 자리에서 바로 새로
    // 선공을 추첨해서 GAME_START까지 보냄(호스트가 직접 눌렀을 때 상대의 REMATCH 메시지를
    // 기다릴 필요 없이 바로 진행되게 하기 위함 - HandleLine의 REMATCH 케이스와 대칭)
    public void SendRematch()
    {
        SendRaw(NetMsg.Rematch);
        if (isHost) ReflipFirstPlayerAndBroadcast();
    }
}

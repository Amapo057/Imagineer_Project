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
/// 지금 스켈레톤에서 일부러 안 한 것(추후 과제로 남김):
///   - 재접속/리매치 전체 흐름 (호스트는 세션 시작 후에도 디스커버리에 계속 응답하게는 해놨지만,
///     끊어진 뒤 실제로 게임 상태를 다시 맞추는 로직은 없음 - Reset 버튼도 아직 네트워크를 모름)
///   - 안드로이드/퀘스트 포팅 시 필요한 WifiManager.MulticastLock + 매니페스트 권한
///     (지금은 PC 두 대로 테스트하는 걸 기준으로 만듦)
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
    public event Action<int, int> OnCardPlayedReceived;

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
        discoveryThread = new Thread(DiscoverLoop) { IsBackground = true };
        discoveryThread.Start();
    }

    void Update()
    {
        while (mainThreadActions.TryDequeue(out var action)) action.Invoke();
    }

    void OnDestroy()
    {
        running = false;

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

        // UnityEngine.Random은 메인 스레드 전용이라 백그라운드 스레드에서는 못 씀 - 순수 .NET Random 사용
        bool hostGoesFirst = new System.Random().NextDouble() < 0.5;
        SendRaw($"{NetMsg.GameStart}|{(hostGoesFirst ? "PEER" : "ME")}");

        var mySide = hostGoesFirst ? PlayerSide.Me : PlayerSide.Opponent;
        mainThreadActions.Enqueue(() => OnGameStartReceived?.Invoke(mySide));

        StartReceiveLoop();
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
            case NetMsg.CardPlayed:
                if (parts.Length >= 3 && int.TryParse(parts[1], out int cardId) && int.TryParse(parts[2], out int laneIdx))
                    mainThreadActions.Enqueue(() => OnCardPlayedReceived?.Invoke(cardId, laneIdx));
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
}

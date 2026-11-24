using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StartNetworkPanel : MonoBehaviour
{
    [SerializeField] private CardDatabase cardDatabase;
    [SerializeField] private Image connectionImage;
    [SerializeField] private TMP_Text localIpText;
    [SerializeField] private TMP_Text remoteIpText;
    [SerializeField] private GameObject options;

    private GameNetworkManager network;
    private string remoteIp = "";
    private bool invalidIp;
    private bool connectionRequired;
    private bool loadingMainGame;

    private void Awake()
    {
        network = GameNetworkManager.Instance;
        network.EnableStartSceneFlow();
        network.ConfigureMatch(cardDatabase);
        network.OnLoadMainGameReceived += LoadMainGame;
    }

    private void OnDestroy()
    {
        if (network != null) network.OnLoadMainGameReceived -= LoadMainGame;
    }

    private void Start()
    {
        // A new round can arrive before this scene finishes loading.
        if (network.HasStartSceneRound) LoadMainGame();
    }

    internal void StartMainGame()
    {
        if (loadingMainGame) return;
        if (network.DeveloperMode) LoadMainGame();
        else if (!network.RequestMainGame()) ShowConnectionRequired();
    }

    private void LoadMainGame()
    {
        if (loadingMainGame) return;
        loadingMainGame = true;
        UnityEngine.SceneManagement.SceneManager.LoadScene("1 MainGame");
    }

    private void Update()
    {
        connectionImage.color = network.Connected || network.DeveloperMode ? Color.green : Color.red;
        localIpText.text = "IP: " + network.LocalAddresses;
        if (network.Connected || network.DeveloperMode) connectionRequired = false;
        remoteIpText.text = network.DeveloperMode ? "개발자 모드 · 단독 플레이" : connectionRequired ? "상대를 먼저 연결해 주세요." : "상대 IP: " + (network.Connected ? network.PeerAddress : remoteIp);
        remoteIpText.color = invalidIp || connectionRequired ? Color.red : Color.white;
    }

    public void AddIpCharacter(string value)
    {
        if (remoteIp.Length < 15) remoteIp += value;
        invalidIp = false;
    }

    public void DeleteIpCharacter()
    {
        if (remoteIp.Length > 0) remoteIp = remoteIp.Substring(0, remoteIp.Length - 1);
        invalidIp = false;
    }

    public void Connect()
    {
        if (remoteIp == "4444") { network.EnableDeveloperMode(); invalidIp = false; }
        else invalidIp = !network.ConnectToHost(remoteIp);
    }
    public void Host() => network.StartManualHost();
    public void Retry() => network.RetryConnection();
    public void ShowConnectionRequired() { connectionRequired = true; options.SetActive(true); }
    public void ToggleOptions() => options.SetActive(!options.activeSelf);
}

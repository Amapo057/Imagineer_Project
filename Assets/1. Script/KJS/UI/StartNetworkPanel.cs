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

    private void Awake()
    {
        network = GameNetworkManager.Instance;
        network.EnableGameplayReadiness();
        network.ConfigureMatch(cardDatabase);
        network.OnLoadMainGameReceived += LoadMainGame;
    }

    private void OnDestroy()
    {
        if (network != null) network.OnLoadMainGameReceived -= LoadMainGame;
    }

    private void LoadMainGame() => UnityEngine.SceneManagement.SceneManager.LoadScene("1 MainGame");

    private void Update()
    {
        connectionImage.color = network.Connected ? Color.green : Color.red;
        localIpText.text = "IP: " + network.LocalAddresses;
        if (network.Connected) connectionRequired = false;
        remoteIpText.text = connectionRequired ? "상대를 먼저 연결해 주세요." : "상대 IP: " + (network.Connected ? network.PeerAddress : remoteIp);
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

    public void Connect() => invalidIp = !network.ConnectToHost(remoteIp);
    public void Host() => network.StartManualHost();
    public void Retry() => network.RetryConnection();
    public void ShowConnectionRequired() { connectionRequired = true; options.SetActive(true); }
    public void ToggleOptions() => options.SetActive(!options.activeSelf);
}

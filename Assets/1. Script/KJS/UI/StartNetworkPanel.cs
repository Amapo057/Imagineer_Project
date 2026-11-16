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

    private void Awake()
    {
        network = GameNetworkManager.Instance;
        network.ConfigureMatch(cardDatabase);
    }

    private void Update()
    {
        connectionImage.color = network.Connected ? Color.green : Color.red;
        localIpText.text = "IP: " + network.LocalAddresses;
        remoteIpText.text = "상대 IP: " + (network.Connected ? network.PeerAddress : remoteIp);
        remoteIpText.color = invalidIp ? Color.red : Color.white;
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
    public void ToggleOptions() => options.SetActive(!options.activeSelf);
}

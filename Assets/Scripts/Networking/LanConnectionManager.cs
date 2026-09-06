using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using TMPro;

public class LanConnectionManager : MonoBehaviour
{
    [Header("LAN Settings")]
    [SerializeField] private string hostIp = "192.168.50.151";
    [SerializeField] private ushort port = 7777;

    [Header("Optional UI")]
    [SerializeField] private TMP_Text statusText;

    private NetworkManager networkManager;
    private UnityTransport transport;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
    }

    private void OnEnable()
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDisable()
    {
        if (networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    public void StartHost()
    {
        if (networkManager == null || transport == null)
        {
            Debug.LogError("[LAN] NetworkManager or UnityTransport missing.");
            return;
        }

        transport.SetConnectionData(
            "192.168.50.151",
            port,
            "0.0.0.0"
        );

        bool started = networkManager.StartHost();

        if (started)
        {
            Debug.Log($"[LAN] HOST started. Port = {port}");
            SetStatus($"HOST\nWaiting for client...\nPort: {port}");
        }
        else
        {
            Debug.LogError("[LAN] Failed to start Host.");
            SetStatus("HOST START FAILED");
        }
    }

    public void StartClient()
    {
        if (networkManager == null || transport == null)
        {
            Debug.LogError("[LAN] NetworkManager or UnityTransport missing.");
            return;
        }

        transport.SetConnectionData(hostIp, port);

        bool started = networkManager.StartClient();

        if (started)
        {
            Debug.Log($"[LAN] CLIENT connecting to {hostIp}:{port}");
            SetStatus($"CLIENT\nConnecting...\n{hostIp}:{port}");
        }
        else
        {
            Debug.LogError("[LAN] Failed to start Client.");
            SetStatus("CLIENT START FAILED");
        }
    }

    public void JoinHost(string ip, ushort targetPort)
    {
        if (networkManager == null || transport == null)
        {
            Debug.LogError("[LAN] NetworkManager or UnityTransport missing.");
            return;
        }

        if (networkManager.IsListening)
        {
            Debug.LogWarning("[LAN] NetworkManager is already running.");
            return;
        }

        transport.SetConnectionData(ip, targetPort);

        bool started = networkManager.StartClient();

        if (started)
        {
            Debug.Log($"[LAN] Connecting to discovered host {ip}:{targetPort}");

            SetStatus(
                $"CLIENT\n" +
                $"Connecting...\n" +
                $"{ip}:{targetPort}"
            );
        }
        else
        {
            Debug.LogError("[LAN] Failed to start Client.");
            SetStatus("CLIENT START FAILED");
        }
    }

    public void JoinLocalhostForTest()
    {
        Debug.Log("[LAN TEST] Connecting directly to 127.0.0.1:7777");
        JoinHost("127.0.0.1", (ushort)7777);
    }

    public void Disconnect()
    {
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
            Debug.Log("[LAN] Disconnected.");
            SetStatus("Disconnected");
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[LAN] Client connected! Client ID = {clientId}");

        if (networkManager.IsHost)
        {
            int count = networkManager.ConnectedClientsIds.Count;

            SetStatus(
                $"HOST\nConnected Clients: {count}\nLast Client ID: {clientId}"
            );
        }
        else if (networkManager.IsClient)
        {
            SetStatus(
                $"CLIENT\nCONNECTED\nClient ID: {clientId}"
            );
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.LogWarning($"[LAN] Client disconnected. ID = {clientId}");

        if (networkManager.IsHost)
        {
            SetStatus(
                $"HOST\nClient disconnected\nRemaining: {networkManager.ConnectedClientsIds.Count}"
            );
        }
        else
        {
            SetStatus("CLIENT\nDISCONNECTED");
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
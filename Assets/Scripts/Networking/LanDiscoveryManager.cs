using UnityEngine;
using Unity.Netcode;

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;

public class LanDiscoveryManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LanConnectionManager lanConnectionManager;

    [Header("Room")]
    [SerializeField] private string roomName = "EyeTracking Room";
    [SerializeField] private ushort gamePort = 7777;
    [SerializeField] private int maxPlayers = 2;

    [Header("Discovery")]
    [SerializeField] private int discoveryPort = 47777;
    [SerializeField] private float searchDurationSeconds = 2f;

    [Header("UI")]
    [SerializeField] private TMP_Text roomResultText;
    [SerializeField] private GameObject joinRoomButton;

    // UI 可以讀這些
    public bool HasDiscoveredRoom { get; private set; }
    public string DiscoveredRoomName { get; private set; }
    public string DiscoveredHostIp { get; private set; }
    public ushort DiscoveredGamePort { get; private set; }
    public int DiscoveredPlayerCount { get; private set; }
    public int DiscoveredMaxPlayers { get; private set; }

    private UdpClient hostUdp;
    private UdpClient searchUdp;

    private CancellationTokenSource hostCancellation;
    private CancellationTokenSource searchCancellation;

    private readonly ConcurrentQueue<RoomResult> roomResults =
        new ConcurrentQueue<RoomResult>();

    private class RoomResult
    {
        public string roomName;
        public string hostIp;
        public ushort gamePort;
        public int playerCount;
        public int maxPlayers;
    }

    // =========================================================
    // HOST DISCOVERY
    // =========================================================

    public void StartAdvertising()
    {
        StopAdvertising();

        try
        {
            hostCancellation = new CancellationTokenSource();

            hostUdp = new UdpClient(discoveryPort);
            hostUdp.EnableBroadcast = true;

            Debug.Log(
                $"[DISCOVERY] Advertising '{roomName}' on UDP {discoveryPort}"
            );

            _ = HostListenLoop(hostCancellation.Token);
        }
        catch (Exception e)
        {
            Debug.LogError(
                $"[DISCOVERY] Failed to start advertising: {e.Message}"
            );
        }
    }

    private async Task HostListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result =
                    await hostUdp.ReceiveAsync();

                string message =
                    Encoding.UTF8.GetString(result.Buffer);

                if (message != "EYE_STUDY_DISCOVER_V1")
                    continue;

                int playerCount = 1;

                if (NetworkManager.Singleton != null &&
                    NetworkManager.Singleton.IsHost)
                {
                    playerCount =
                        NetworkManager.Singleton.ConnectedClientsIds.Count;
                }

                string response =
                    $"EYE_STUDY_ROOM_V1|" +
                    $"{roomName}|" +
                    $"{gamePort}|" +
                    $"{playerCount}|" +
                    $"{maxPlayers}";

                byte[] responseBytes =
                    Encoding.UTF8.GetBytes(response);

                await hostUdp.SendAsync(
                    responseBytes,
                    responseBytes.Length,
                    result.RemoteEndPoint
                );

                Debug.Log(
                    $"[DISCOVERY] Replied to {result.RemoteEndPoint.Address}"
                );
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.LogWarning(
                        $"[DISCOVERY] Host receive error: {e.Message}"
                    );
                }
            }
        }
    }

    public void StopAdvertising()
    {
        hostCancellation?.Cancel();

        hostUdp?.Close();
        hostUdp?.Dispose();

        hostUdp = null;

        hostCancellation?.Dispose();
        hostCancellation = null;
    }

    // =========================================================
    // CLIENT DISCOVERY
    // =========================================================

    public void FindRooms()
    {
        _ = FindRoomsAsync();
    }

    private async Task FindRoomsAsync()
    {
        StopSearching();

        ClearDiscoveredRoom();

        try
        {
            searchCancellation = new CancellationTokenSource();

            // 0 = OS 自動分配暫時 Port
            searchUdp = new UdpClient(0);
            searchUdp.EnableBroadcast = true;

            byte[] request =
                Encoding.UTF8.GetBytes(
                    "EYE_STUDY_DISCOVER_V1"
                );

            IPEndPoint broadcastEndpoint =
                new IPEndPoint(
                    IPAddress.Broadcast,
                    discoveryPort
                );

            await searchUdp.SendAsync(
                request,
                request.Length,
                broadcastEndpoint
            );

            Debug.Log(
                $"[DISCOVERY] Searching LAN on UDP {discoveryPort}..."
            );

            Task receiveTask =
                ClientReceiveLoop(
                    searchCancellation.Token
                );

            await Task.Delay(
                TimeSpan.FromSeconds(searchDurationSeconds)
            );

            StopSearching();

            Debug.Log("[DISCOVERY] Search finished.");
        }
        catch (Exception e)
        {
            Debug.LogWarning(
                $"[DISCOVERY] Search error: {e.Message}"
            );

            StopSearching();
        }
    }

    private async Task ClientReceiveLoop(
        CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result =
                    await searchUdp.ReceiveAsync();

                string message =
                    Encoding.UTF8.GetString(
                        result.Buffer
                    );

                ParseRoomResponse(
                    message,
                    result.RemoteEndPoint.Address.ToString()
                );
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                {
                    Debug.LogWarning(
                        $"[DISCOVERY] Client receive error: {e.Message}"
                    );
                }

                break;
            }
        }
    }

    private void ParseRoomResponse(
        string message,
        string senderIp)
    {
        string[] parts =
            message.Split('|');

        if (parts.Length != 5)
            return;

        if (parts[0] != "EYE_STUDY_ROOM_V1")
            return;

        if (!ushort.TryParse(
                parts[2],
                out ushort parsedPort))
            return;

        if (!int.TryParse(
                parts[3],
                out int playerCount))
            return;

        if (!int.TryParse(
                parts[4],
                out int parsedMaxPlayers))
            return;

        RoomResult room =
            new RoomResult
            {
                roomName = parts[1],
                hostIp = senderIp,
                gamePort = parsedPort,
                playerCount = playerCount,
                maxPlayers = parsedMaxPlayers
            };

        roomResults.Enqueue(room);
    }

    private void StopSearching()
    {
        searchCancellation?.Cancel();

        searchUdp?.Close();
        searchUdp?.Dispose();

        searchUdp = null;

        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    // =========================================================
    // UNITY MAIN THREAD
    // =========================================================

    private void Start()
    {
        if (roomResultText != null)
            roomResultText.text = "No room found";

        if (joinRoomButton != null)
            joinRoomButton.SetActive(false);
    }
    
    private void Update()
    {
        while (roomResults.TryDequeue(
                   out RoomResult room))
        {
            HasDiscoveredRoom = true;

            DiscoveredRoomName =
                room.roomName;

            DiscoveredHostIp =
                room.hostIp;

            DiscoveredGamePort =
                room.gamePort;

            DiscoveredPlayerCount =
                room.playerCount;

            DiscoveredMaxPlayers =
                room.maxPlayers;

            Debug.Log(
                $"[DISCOVERY] Found room: " +
                $"{room.roomName} " +
                $"{room.playerCount}/{room.maxPlayers} " +
                $"{room.hostIp}:{room.gamePort}"
            );

            if (roomResultText != null)
            {
                roomResultText.text =
                    $"{room.roomName}\n" +
                    $"{room.playerCount} / {room.maxPlayers}";
            }

            if (joinRoomButton != null)
            {
                joinRoomButton.SetActive(
                    room.playerCount < room.maxPlayers
                );
            }
        }
    }

    // =========================================================
    // JOIN
    // =========================================================

    public void JoinDiscoveredRoom()
    {
        if (!HasDiscoveredRoom)
        {
            Debug.LogWarning(
                "[DISCOVERY] No discovered room."
            );

            return;
        }

        if (DiscoveredPlayerCount >=
            DiscoveredMaxPlayers)
        {
            Debug.LogWarning(
                "[DISCOVERY] Room is full."
            );

            return;
        }

        lanConnectionManager.JoinHost(
            DiscoveredHostIp,
            DiscoveredGamePort
        );
    }

    private void ClearDiscoveredRoom()
    {
        HasDiscoveredRoom = false;

        DiscoveredRoomName = "";
        DiscoveredHostIp = "";
        DiscoveredGamePort = 0;
        DiscoveredPlayerCount = 0;
        DiscoveredMaxPlayers = 0;

        while (roomResults.TryDequeue(out _))
        {
        }
    }

    private void OnDestroy()
    {
        StopAdvertising();
        StopSearching();
    }
}
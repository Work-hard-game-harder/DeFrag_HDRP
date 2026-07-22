using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LobbyManager : MonoBehaviour
{
    [Header("Join UI")]
    [SerializeField] private TextMeshProUGUI joinCodeText;
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Scene Flow")]
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private string gameplaySceneName = "B5F";

    [Header("Network Prefabs")]
    [SerializeField] private GameObject lobbyAvatarPrefab;
    [SerializeField] private GameObject gameplayPlayerPrefab;

    [Header("Room")]
    [Min(1)] [SerializeField] private int maxClientConnections = 1;

    public static LobbyManager Instance { get; private set; }
    public static string SavedJoinCode { get; private set; }

    private readonly Dictionary<ulong, NetworkObject> lobbyAvatars = new();
    private NetworkManager networkManager;
    private bool isStartingSession;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        try
        {
            await EnsureServicesInitializedAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError($"서비스 초기화 실패: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            UnsubscribeFromNetworkEvents();
            Instance = null;
        }
    }

    public async void BeginHostFlow()
    {
        if (!CanStartSession())
        {
            return;
        }

        isStartingSession = true;

        try
        {
            await EnsureServicesInitializedAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxClientConnections);
            SavedJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            NetworkManager manager = PrepareNetworkManager();
            UnityTransport transport = GetTransport(manager);
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                true);

            if (!manager.StartHost())
            {
                throw new InvalidOperationException("NetworkManager가 호스트를 시작하지 못했습니다.");
            }

            SubscribeToNetworkEvents(manager);
            SetJoinCodeText(SavedJoinCode);
            manager.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            Debug.LogError($"방 생성 실패: {exception.Message}");
            UnsubscribeFromNetworkEvents();
        }
        finally
        {
            isStartingSession = false;
        }
    }

    public async void JoinWithRelay()
    {
        string joinCode = joinCodeInput == null ? string.Empty : NormalizeJoinCode(joinCodeInput.text);
        if (!CanStartSession() || string.IsNullOrEmpty(joinCode))
        {
            Debug.LogWarning("입장할 방 코드를 입력하세요.");
            return;
        }

        isStartingSession = true;

        try
        {
            await EnsureServicesInitializedAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            NetworkManager manager = PrepareNetworkManager();
            UnityTransport transport = GetTransport(manager);
            transport.SetClientRelayData(
                allocation.RelayServer.IpV4,
                (ushort)allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                allocation.HostConnectionData,
                true);

            SavedJoinCode = joinCode;
            if (!manager.StartClient())
            {
                throw new InvalidOperationException("NetworkManager가 클라이언트를 시작하지 못했습니다.");
            }

            SubscribeToNetworkEvents(manager);
        }
        catch (RelayServiceException exception)
        {
            Debug.LogError($"방 코드 접속 실패: {exception.Message}");
            UnsubscribeFromNetworkEvents();
        }
        catch (Exception exception)
        {
            Debug.LogError($"클라이언트 시작 실패: {exception.Message}");
            UnsubscribeFromNetworkEvents();
        }
        finally
        {
            isStartingSession = false;
        }
    }

    private bool CanStartSession()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return !isStartingSession && (manager == null || !manager.IsListening);
    }

    private NetworkManager PrepareNetworkManager()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            throw new InvalidOperationException("NetworkManager를 찾을 수 없습니다.");
        }

        if (lobbyAvatarPrefab == null || gameplayPlayerPrefab == null)
        {
            throw new InvalidOperationException("로비 또는 게임 플레이어 프리팹이 설정되지 않았습니다.");
        }

        manager.NetworkConfig.PlayerPrefab = null;
        networkManager = manager;
        return manager;
    }

    private static UnityTransport GetTransport(NetworkManager manager)
    {
        UnityTransport transport = manager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            throw new InvalidOperationException("NetworkManager에 UnityTransport가 없습니다.");
        }

        return transport;
    }

    private void SubscribeToNetworkEvents(NetworkManager manager)
    {
        UnsubscribeFromNetworkEvents();
        networkManager = manager;
        manager.OnClientConnectedCallback += HandleClientConnected;
        manager.OnClientDisconnectCallback += HandleClientDisconnected;
        manager.SceneManager.OnLoadEventCompleted += HandleNetworkSceneLoaded;
    }

    private void UnsubscribeFromNetworkEvents()
    {
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadEventCompleted -= HandleNetworkSceneLoaded;
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (SceneManager.GetActiveScene().name == lobbySceneName)
        {
            SpawnLobbyAvatar(clientId);
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (!lobbyAvatars.Remove(clientId, out NetworkObject avatar) || avatar == null)
        {
            return;
        }

        if (avatar.IsSpawned && networkManager != null && networkManager.IsServer)
        {
            avatar.Despawn(true);
        }
    }

    private void HandleNetworkSceneLoaded(
        string sceneName,
        LoadSceneMode loadMode,
        List<ulong> clientsCompleted,
        List<ulong> clientsTimedOut)
    {
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        if (sceneName == lobbySceneName)
        {
            lobbyAvatars.Clear();
            foreach (ulong clientId in networkManager.ConnectedClientsIds)
            {
                SpawnLobbyAvatar(clientId);
            }
        }
        else if (sceneName == gameplaySceneName)
        {
            lobbyAvatars.Clear();
            SpawnGameplayPlayers();
        }

        foreach (ulong timedOutClientId in clientsTimedOut)
        {
            Debug.LogWarning($"씬 전환 시간 초과 클라이언트: {timedOutClientId}");
        }
    }

    private void SpawnLobbyAvatar(ulong clientId)
    {
        if (lobbyAvatars.ContainsKey(clientId))
        {
            return;
        }

        LobbyAvatarSpawnPoints spawnPoints = LobbyAvatarSpawnPoints.Instance;
        if (spawnPoints == null)
        {
            Debug.LogError("LobbyAvatarSpawnPoints가 LobbyScene에 없습니다.");
            return;
        }

        Transform spawnPoint = spawnPoints.GetSpawnPoint(clientId == NetworkManager.ServerClientId);
        GameObject instance = Instantiate(lobbyAvatarPrefab, spawnPoint.position, spawnPoint.rotation);
        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        networkObject.SpawnWithOwnership(clientId, true);
        lobbyAvatars.Add(clientId, networkObject);
    }

    private void SpawnGameplayPlayers()
    {
        GameplaySpawnPointRegistry spawnPoints = GameplaySpawnPointRegistry.Instance;
        if (spawnPoints == null)
        {
            Debug.LogError("GameplaySpawnPointRegistry가 게임 씬에 없습니다.");
            return;
        }

        int spawnIndex = 0;
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (networkManager.ConnectedClients[clientId].PlayerObject != null)
            {
                continue;
            }

            Transform spawnPoint = spawnPoints.GetSpawnPoint(spawnIndex++);
            GameObject instance = Instantiate(gameplayPlayerPrefab, spawnPoint.position, spawnPoint.rotation);
            instance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
        }
    }

    private void SetJoinCodeText(string joinCode)
    {
        if (joinCodeText != null)
        {
            joinCodeText.text = $"CODE: {joinCode}";
        }
    }

    private static string NormalizeJoinCode(string joinCode)
    {
        return joinCode.Trim().ToUpperInvariant();
    }

    private static async Task EnsureServicesInitializedAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }
}

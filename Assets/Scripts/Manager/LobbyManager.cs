using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class LobbyManager : MonoBehaviour
{
    private const string RelayJoinCodeCharacters = "6789BCDFGHJKLMNPQRTW";

    private enum RelayConnectionProtocol
    {
        Udp,
        Dtls,
        Wss
    }

    [Header("Join UI")]
    [SerializeField] private TextMeshProUGUI joinCodeText;
    [SerializeField] private TMP_InputField joinCodeInput;

    [Header("Scene Flow")]
    [SerializeField] private string lobbySceneName = "LobbyScene";
    [SerializeField] private string transportFailureSceneName = "MainLobby";
    [SerializeField] private GameObject warningText;    

    [Header("Network Prefabs")]
    [SerializeField] private GameObject lobbyAvatarPrefab;
    [SerializeField] private GameObject gameplayPlayerPrefab;

    [Header("Room")]
    [Min(1)] [SerializeField] private int maxClientConnections = 1;
    [SerializeField] private RelayConnectionProtocol relayProtocol = RelayConnectionProtocol.Dtls;

    public static LobbyManager Instance { get; private set; }
    public static string SavedJoinCode { get; private set; }

    private readonly Dictionary<ulong, NetworkObject> lobbyAvatars = new();
    private NetworkManager networkManager;
    private bool isStartingSession;
    private bool isReturningToMainLobby;

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

    private void OnEnable()
    {
        if (warningText != null)
        {
            warningText.SetActive(false);
        }

        if (joinCodeInput != null)
        {
            joinCodeInput.onSubmit.AddListener(HandleJoinCodeSubmitted);
        }
    }

    private void OnDisable()
    {
        if (joinCodeInput != null)
        {
            joinCodeInput.onSubmit.RemoveListener(HandleJoinCodeSubmitted);
        }
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
            AudioManager.Instance.PlaySFX("Button1");
            await EnsureServicesInitializedAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxClientConnections);
            SavedJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            NetworkManager manager = PrepareNetworkManager();
            UnityTransport transport = GetTransport(manager);
            ConfigureTransportProtocol(transport);
            transport.SetRelayServerData(CreateRelayServerData(allocation));

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
        if (!CanStartSession())
        {
            return;
        }

        if (!IsValidJoinCode(joinCode))
        {
            AudioManager.Instance.PlaySFX("WrongNumber");
            warningText.SetActive(true);
            Debug.LogWarning("올바른 Relay 참가 코드를 입력하세요. 코드를 복사할 때 문자가 바뀌지 않았는지 확인하세요.");
            joinCodeInput?.ActivateInputField();
            return;
        }

        isStartingSession = true;
        joinCodeInput.text = joinCode;
        joinCodeInput.interactable = false;

        try
        {
            await EnsureServicesInitializedAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            NetworkManager manager = PrepareNetworkManager();
            UnityTransport transport = GetTransport(manager);
            ConfigureTransportProtocol(transport);
            transport.SetRelayServerData(CreateRelayServerData(allocation));

            SavedJoinCode = joinCode;
            if (!manager.StartClient())
            {
                throw new InvalidOperationException("NetworkManager가 클라이언트를 시작하지 못했습니다.");
            }

            SubscribeToNetworkEvents(manager);
        }
        catch (RelayServiceException exception)
        {
            Debug.LogError($"Relay 방 참가에 실패했습니다. 방 코드와 호스트 상태를 확인하세요. (ErrorCode: {exception.ErrorCode})");
            ResetFailedClientStart();
            UnsubscribeFromNetworkEvents();
        }
        catch (Exception exception)
        {
            Debug.LogError($"클라이언트 시작 실패: {exception.Message}");
            ResetFailedClientStart();
            UnsubscribeFromNetworkEvents();
        }
        finally
        {
            isStartingSession = false;
            if (joinCodeInput != null)
            {
                joinCodeInput.interactable = true;
            }
        }
    }

    private void HandleJoinCodeSubmitted(string _)
    {
        JoinWithRelay();
    }

    private void ResetFailedClientStart()
    {
        SavedJoinCode = null;

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && !manager.ShutdownInProgress)
        {
            manager.Shutdown();
        }
    }

    private bool CanStartSession()
    {
        NetworkManager manager = NetworkManager.Singleton;
        return !isStartingSession && !isReturningToMainLobby &&
               (manager == null || !manager.IsListening);
    }

    public void ReturnToMainLobby()
    {
        if (!isReturningToMainLobby)
        {
            StartCoroutine(ReturnToMainLobbyRoutine());
        }
    }

    private IEnumerator ReturnToMainLobbyRoutine()
    {
        isReturningToMainLobby = true;
        isStartingSession = false;

        NetworkManager manager = NetworkManager.Singleton;
        UnsubscribeFromNetworkEvents();
        lobbyAvatars.Clear();
        SavedJoinCode = null;

        if (manager != null && manager.IsListening && !manager.ShutdownInProgress)
        {
            manager.Shutdown();
        }

        while (manager != null && manager.ShutdownInProgress)
        {
            yield return null;
        }

        networkManager = null;
        Instance = null;
        Destroy(gameObject);
        SceneManager.LoadScene(transportFailureSceneName, LoadSceneMode.Single);
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
        manager.OnTransportFailure += HandleTransportFailure;
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
        networkManager.OnTransportFailure -= HandleTransportFailure;
        if (networkManager.SceneManager != null)
        {
            networkManager.SceneManager.OnLoadEventCompleted -= HandleNetworkSceneLoaded;
        }
    }

    private void HandleTransportFailure()
    {
        Debug.LogError("Relay 전송 연결이 끊어졌습니다. 기존 할당을 폐기하고 메인 로비로 돌아갑니다.");
        StartCoroutine(ResetSessionAfterTransportFailure());
    }

    private IEnumerator ResetSessionAfterTransportFailure()
    {
        NetworkManager failedManager = networkManager;
        UnsubscribeFromNetworkEvents();
        lobbyAvatars.Clear();
        SavedJoinCode = null;
        isStartingSession = false;

        yield return null;

        if (failedManager != null && failedManager.IsListening && !failedManager.ShutdownInProgress)
        {
            failedManager.Shutdown();
        }

        while (failedManager != null && failedManager.ShutdownInProgress)
        {
            yield return null;
        }

        if (SceneManager.GetActiveScene().name != transportFailureSceneName)
        {
            SceneManager.LoadScene(transportFailureSceneName, LoadSceneMode.Single);
        }
    }

    private RelayServerData CreateRelayServerData(Allocation allocation)
    {
        return allocation.ToRelayServerData(GetRelayConnectionType());
    }

    private RelayServerData CreateRelayServerData(JoinAllocation allocation)
    {
        return allocation.ToRelayServerData(GetRelayConnectionType());
    }

    private void ConfigureTransportProtocol(UnityTransport transport)
    {
        // RelayServerData의 프로토콜과 UnityTransport가 사용하는 네트워크
        // 인터페이스는 반드시 일치해야 한다. WSS만 WebSocket 인터페이스를 사용한다.
        transport.UseWebSockets = relayProtocol == RelayConnectionProtocol.Wss;
    }

    private string GetRelayConnectionType()
    {
        return relayProtocol switch
        {
            RelayConnectionProtocol.Udp => "udp",
            RelayConnectionProtocol.Dtls => "dtls",
            RelayConnectionProtocol.Wss => "wss",
            _ => throw new ArgumentOutOfRangeException()
        };
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
        if (networkManager != null &&
            !networkManager.IsServer &&
            clientId == networkManager.LocalClientId)
        {
            Debug.Log("호스트와의 연결이 종료되어 메인 로비로 돌아갑니다.");
            ReturnToMainLobby();
            return;
        }

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
        else if (GameplaySpawnPointRegistry.Instance != null)
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

    private static bool IsValidJoinCode(string joinCode)
    {
        if (joinCode.Length < 6 || joinCode.Length > 12)
        {
            return false;
        }

        foreach (char character in joinCode)
        {
            if (RelayJoinCodeCharacters.IndexOf(character) < 0)
            {
                return false;
            }
        }

        return true;
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

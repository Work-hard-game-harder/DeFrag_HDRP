using System.Collections;
using System.Collections.Generic;
using DeFrag.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace DeFrag.Combat
{
    /// <summary>
    /// 서버에서 한 플레이어의 사망을 파티 전체 실패로 확정합니다.
    /// 로컬 카메라와 UI는 각 클라이언트의 Presentation이 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerStats))]
    public sealed class NetworkPartyFailureController : NetworkBehaviour
    {
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private GameObject deathScreenPrefab;
        [SerializeField, Min(0f)] private float playerDespawnDelay = 0.15f;
        [SerializeField, Min(0.01f)] private float uiFadeDuration = 0.5f;

        private static bool failureInProgress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetFailureState()
        {
            failureInProgress = false;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            failureInProgress = false;
        }

        private void Awake()
        {
            if (playerStats == null)
                playerStats = GetComponent<PlayerStats>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && playerStats != null)
                playerStats.Died += HandlePlayerDied;
        }

        public override void OnNetworkDespawn()
        {
            if (playerStats != null)
                playerStats.Died -= HandlePlayerDied;
        }

        private void HandlePlayerDied()
        {
            if (!IsServer || failureInProgress)
                return;

            failureInProgress = true;
            ShowPartyFailureClientRpc();
            StartCoroutine(DespawnPartyPlayers());
        }

        [ClientRpc]
        private void ShowPartyFailureClientRpc()
        {
            PartyFailurePresentation.Show(IsHost, deathScreenPrefab, uiFadeDuration);
        }

        private IEnumerator DespawnPartyPlayers()
        {
            yield return new WaitForSecondsRealtime(playerDespawnDelay);

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
                yield break;

            var players = new List<NetworkObject>();
            foreach (ulong clientId in manager.ConnectedClientsIds)
            {
                NetworkObject playerObject = manager.ConnectedClients[clientId].PlayerObject;
                if (playerObject != null && playerObject.IsSpawned)
                    players.Add(playerObject);
            }

            // 이 코루틴을 소유한 오브젝트는 마지막에 제거해야 나머지도 확실히 Despawn됩니다.
            for (int i = 0; i < players.Count; i++)
            {
                NetworkObject playerObject = players[i];
                if (playerObject != NetworkObject && playerObject.IsSpawned)
                    playerObject.Despawn(true);
            }

            if (NetworkObject != null && NetworkObject.IsSpawned)
                NetworkObject.Despawn(true);
        }
    }

    /// <summary>각 기기에서만 존재하는 사망 카메라와 UI입니다.</summary>
    internal sealed class PartyFailurePresentation : MonoBehaviour
    {
        private const string PresentationName = "Party Failure Presentation";

        private Camera frozenCamera;
        private Vector3 frozenPosition;
        private Quaternion frozenRotation;

        public static void Show(bool showHostButtons, GameObject deathScreenPrefab, float fadeDuration)
        {
            if (FindAnyObjectByType<PartyFailurePresentation>() != null)
                return;

            GameObject root = new GameObject(PresentationName);
            PartyFailurePresentation presentation = root.AddComponent<PartyFailurePresentation>();
            presentation.FreezeLocalCamera(root.transform);
            presentation.CreateUi(deathScreenPrefab, showHostButtons, fadeDuration);

            GameplayInputGate.TryAcquire(presentation);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (frozenCamera == null)
                return;

            frozenCamera.transform.SetPositionAndRotation(frozenPosition, frozenRotation);
        }

        private void OnDestroy()
        {
            GameplayInputGate.Release(this);
        }

        private void FreezeLocalCamera(Transform presentationRoot)
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject localPlayer = manager != null ? manager.LocalClient?.PlayerObject : null;
            Camera sourceCamera = localPlayer != null
                ? localPlayer.GetComponentInChildren<Camera>(true)
                : Camera.main;

            if (sourceCamera == null)
                return;

            frozenPosition = sourceCamera.transform.position;
            frozenRotation = sourceCamera.transform.rotation;
            sourceCamera.transform.SetParent(presentationRoot, true);
            frozenCamera = sourceCamera;
        }

        private void CreateUi(GameObject deathScreenPrefab, bool showHostButtons, float fadeDuration)
        {
            if (deathScreenPrefab == null)
            {
                Debug.LogError("DeathScreen 프리팹이 PlayerCharacter(H)에 설정되지 않았습니다.");
                return;
            }

            GameObject instance = Instantiate(deathScreenPrefab, transform, false);
            PartyFailureView view = instance.GetComponent<PartyFailureView>();
            if (view == null)
            {
                Debug.LogError("DeathScreen 프리팹에 PartyFailureView가 없습니다.");
                return;
            }

            view.Initialize(
                showHostButtons,
                fadeDuration,
                ReturnPartyToMainLobby,
                OpenStageSelection);

            EnsureEventSystem();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();
        }

        private static void ReturnPartyToMainLobby()
        {
            if (LobbyManager.Instance != null)
                LobbyManager.Instance.ReturnToMainLobby();
        }

        private static void OpenStageSelection()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.SceneManager == null)
                return;

            SceneChange.RequestOpenStageSelectionOnNextLoad();
            manager.SceneManager.LoadScene("LobbyScene", LoadSceneMode.Single);
        }
    }
}

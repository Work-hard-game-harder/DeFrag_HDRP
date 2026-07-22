using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class SpawnManager : MonoBehaviour
{
    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 로비씬은 자동 스폰 + PlayerController.OnNetworkSpawn으로 처리
        if (scene.name == "LobbyScene") return;

        // 다른 씬에서는 새로 생성
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");
        if (spawnPoints.Length == 0) return;

        int index = Random.Range(0, spawnPoints.Length);
        Vector3 pos = spawnPoints[index].transform.position;
        Quaternion rot = spawnPoints[index].transform.rotation;

        // 기존 플레이어 제거
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (localPlayer != null)
        {
            localPlayer.Despawn();
        }

        // 새 플레이어 생성
        var playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var playerInstance = Instantiate(playerPrefab, pos, rot);
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId);

        // 데이터 이어받기
        var stats = playerInstance.GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.ApplyData(GameDataManager.Instance.Health, GameDataManager.Instance.Inventory);
        }
    }
}

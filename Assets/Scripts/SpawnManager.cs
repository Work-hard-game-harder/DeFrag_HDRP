using Unity.Netcode;
using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    private void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        // 씬에서 "SpawnPoint" 태그를 가진 오브젝트들을 모두 찾기
        GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

        if (spawnPoints.Length == 0)
        {
            Debug.LogWarning("SpawnPoint 태그가 붙은 오브젝트가 없습니다!");
            return;
        }

        // 랜덤으로 하나 선택 (원하면 순차적으로도 가능)
        int index = Random.Range(0, spawnPoints.Length);
        Vector3 pos = spawnPoints[index].transform.position;
        Quaternion rot = spawnPoints[index].transform.rotation;

        // Player Prefab 가져와서 지정된 위치에 생성
        var playerPrefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        var playerInstance = Instantiate(playerPrefab, pos, rot);

        // 네트워크에 등록
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
    }
}

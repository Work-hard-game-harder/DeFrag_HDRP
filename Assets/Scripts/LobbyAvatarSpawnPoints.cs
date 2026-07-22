using UnityEngine;

public sealed class LobbyAvatarSpawnPoints : MonoBehaviour
{
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;

    public static LobbyAvatarSpawnPoints Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public Transform GetSpawnPoint(bool isHost)
    {
        Transform spawnPoint = isHost ? hostSpawnPoint : clientSpawnPoint;
        if (spawnPoint == null)
        {
            throw new System.InvalidOperationException("로비 아바타 스폰 포인트가 설정되지 않았습니다.");
        }

        return spawnPoint;
    }
}

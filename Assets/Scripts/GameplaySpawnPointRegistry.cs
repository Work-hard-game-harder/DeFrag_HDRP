using UnityEngine;

public sealed class GameplaySpawnPointRegistry : MonoBehaviour
{
    [Header("Two-player role spawn points")]
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;

    [Header("Legacy/fallback spawn points")]
    [SerializeField] private Transform[] spawnPoints;

    public static GameplaySpawnPointRegistry Instance { get; private set; }

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

    public Transform GetSpawnPoint(int index)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            throw new System.InvalidOperationException("게임 플레이어 스폰 포인트가 설정되지 않았습니다.");
        }

        return spawnPoints[index % spawnPoints.Length];
    }

    public Transform GetSpawnPoint(bool isHost)
    {
        Transform roleSpawnPoint = isHost ? hostSpawnPoint : clientSpawnPoint;
        if (roleSpawnPoint != null)
            return roleSpawnPoint;

        return GetSpawnPoint(isHost ? 0 : 1);
    }
}

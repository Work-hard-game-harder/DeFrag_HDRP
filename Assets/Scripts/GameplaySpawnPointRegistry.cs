using UnityEngine;
using DeFrag.B1F;

public sealed class GameplaySpawnPointRegistry : MonoBehaviour
{
    [Header("Two-player role spawn points")]
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;

    [Header("Legacy/fallback spawn points")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("B1F checkpoint role spawn points")]
    [SerializeField] private Transform distributionBoxAHostSpawnPoint;
    [SerializeField] private Transform distributionBoxAClientSpawnPoint;
    [SerializeField] private Transform controlRoomHostSpawnPoint;
    [SerializeField] private Transform controlRoomClientSpawnPoint;
    [SerializeField] private Transform generatorHostSpawnPoint;
    [SerializeField] private Transform generatorClientSpawnPoint;

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
        Transform checkpointSpawnPoint = GetCheckpointSpawnPoint(isHost);
        if (checkpointSpawnPoint != null)
            return checkpointSpawnPoint;

        Transform roleSpawnPoint = isHost ? hostSpawnPoint : clientSpawnPoint;
        if (roleSpawnPoint != null)
            return roleSpawnPoint;

        return GetSpawnPoint(isHost ? 0 : 1);
    }

    private Transform GetCheckpointSpawnPoint(bool isHost)
    {
        B1FCheckpointTracker tracker = B1FCheckpointTracker.Instance;
        if (tracker == null)
            return null;

        return tracker.Current switch
        {
            B1FCheckpoint.DistributionBoxACompleted => isHost
                ? distributionBoxAHostSpawnPoint : distributionBoxAClientSpawnPoint,
            B1FCheckpoint.ConnectServerBreachCompleted => isHost
                ? controlRoomHostSpawnPoint : controlRoomClientSpawnPoint,
            B1FCheckpoint.GeneratorCompleted => isHost
                ? generatorHostSpawnPoint : generatorClientSpawnPoint,
            _ => null
        };
    }
}

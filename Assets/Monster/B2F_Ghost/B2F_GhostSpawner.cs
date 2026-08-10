using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class B2F_GhostSpawner : MonoBehaviour
{
    [Header("Spawn Configuration")]
    [SerializeField] private NetworkObject ghostPrefab;
    [FormerlySerializedAs("spawnPoint")]
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private bool spawnWhenServerIsReady = true;
    [SerializeField] private bool repeatSpawning = true;
    [Min(0.05f)]
    [SerializeField] private float spawnDelay = 5f;

    private Coroutine automaticSpawnRoutine;

    private void Start()
    {
        if (spawnWhenServerIsReady)
            automaticSpawnRoutine = StartCoroutine(SpawnWhenServerIsReady());
    }

    private void OnDisable()
    {
        if (automaticSpawnRoutine == null)
            return;

        StopCoroutine(automaticSpawnRoutine);
        automaticSpawnRoutine = null;
    }

    public NetworkObject SpawnGhost()
    {
        Transform point = SelectRandomSpawnPoint();
        return SpawnGhost(point.position, point.rotation);
    }

    public NetworkObject SpawnGhost(Vector3 position, Quaternion rotation)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer)
        {
            Debug.LogWarning("[B2F Ghost Spawner] Ghosts can only be spawned by the active server.", this);
            return null;
        }

        if (ghostPrefab == null)
        {
            Debug.LogError("[B2F Ghost Spawner] Ghost Prefab is not assigned.", this);
            return null;
        }

        NetworkObject instance = Instantiate(ghostPrefab, position, rotation);
        instance.Spawn(true);
        return instance;
    }

    public void RestartAutomaticSpawning()
    {
        if (automaticSpawnRoutine != null)
            StopCoroutine(automaticSpawnRoutine);

        automaticSpawnRoutine = StartCoroutine(SpawnWhenServerIsReady());
    }

    private IEnumerator SpawnWhenServerIsReady()
    {
        while (NetworkManager.Singleton == null ||
               !NetworkManager.Singleton.IsListening)
        {
            yield return null;
        }

        if (!NetworkManager.Singleton.IsServer)
            yield break;

        do
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, spawnDelay));

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer)
                break;

            NetworkObject spawnedGhost = SpawnGhost();
            if (spawnedGhost == null)
                continue;

            if (!repeatSpawning)
                break;

            // Start the next delay only after this ghost has been despawned.
            while (spawnedGhost != null && spawnedGhost.IsSpawned &&
                   isActiveAndEnabled)
            {
                yield return null;
            }
        }
        while (repeatSpawning && isActiveAndEnabled);

        automaticSpawnRoutine = null;
    }

    private Transform SelectRandomSpawnPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
            return transform;

        int randomStartIndex = Random.Range(0, spawnPoints.Length);
        for (int offset = 0; offset < spawnPoints.Length; offset++)
        {
            int index = (randomStartIndex + offset) % spawnPoints.Length;
            if (spawnPoints[index] != null)
                return spawnPoints[index];
        }

        return transform;
    }

    private void OnValidate()
    {
        spawnDelay = Mathf.Max(0.05f, spawnDelay);
    }
}

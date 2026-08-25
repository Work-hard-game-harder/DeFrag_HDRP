using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    public sealed class B1FStoryDebugCheckpoint : MonoBehaviour
    {
        [Header("Editor / Development Build Only")]
        [Tooltip("Starts the scene through the authoritative Distribution Box A completion path.")]
        [SerializeField] private bool startAfterDistributionBoxA;
        [SerializeField] private DistributionBoxController distributionBoxA;
        [SerializeField, Min(1f)] private float networkReadyTimeout = 20f;

        private IEnumerator Start()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!startAfterDistributionBoxA)
                yield break;

            if (distributionBoxA == null)
            {
                Debug.LogError(
                    "[B1F Story Debug] Distribution Box A is not assigned.",
                    this);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + networkReadyTimeout;
            while (!IsNetworkReady() && Time.realtimeSinceStartup < deadline)
                yield return null;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening)
            {
                Debug.LogError(
                    "[B1F Story Debug] Network session did not become ready before the timeout.",
                    this);
                yield break;
            }

            // Every peer owns this scene object, but only the server may mutate
            // DistributionBoxController NetworkVariables or spawn the monster.
            if (!manager.IsServer)
                yield break;

            while (!distributionBoxA.IsSpawned && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!distributionBoxA.IsSpawned)
            {
                Debug.LogError(
                    "[B1F Story Debug] Distribution Box A NetworkObject was not spawned.",
                    this);
                yield break;
            }

            if (!distributionBoxA.IsBoxA)
            {
                Debug.LogError(
                    "[B1F Story Debug] The assigned distribution box is not configured as Box A.",
                    distributionBoxA);
                yield break;
            }

            if (!distributionBoxA.TryCompleteForStoryDebugServer())
            {
                Debug.LogWarning(
                    "[B1F Story Debug] Box A was already complete or could not enter the debug checkpoint.",
                    distributionBoxA);
                yield break;
            }

            Debug.Log(
                "[B1F Story Debug] Applied checkpoint: AFTER DISTRIBUTION BOX A. " +
                "The normal success path will unlock the door, transition to EmergencyPower, " +
                "and spawn the TV monster.",
                this);
#else
            yield break;
#endif
        }

        private bool IsNetworkReady()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening;
        }
    }
}

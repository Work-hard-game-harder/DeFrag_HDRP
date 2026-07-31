using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Player
{
    public sealed class OwnerCharacterVisibility : NetworkBehaviour
    {
        [Header("Owner-only camera visibility")]
        [SerializeField] private Renderer[] characterRenderers;
        [Tooltip("Select exactly one layer that the local gameplay cameras do not render.")]
        [SerializeField] private LayerMask ownerHiddenLayer;
        [Tooltip("Use only for a standalone test-scene player that is not spawned by NGO.")]
        [SerializeField] private bool applyAsLocalWhenNotNetworkSpawned;

        private int[] originalLayers;
        private bool ownerVisibilityApplied;

        private void Start()
        {
            if (!IsSpawned && applyAsLocalWhenNotNetworkSpawned)
            {
                CaptureOriginalLayers();
                ApplyOwnerHiddenLayer();
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            RestoreOriginalLayers();
            CaptureOriginalLayers();

            if (IsOwner)
                ApplyOwnerHiddenLayer();
        }

        public override void OnNetworkDespawn()
        {
            RestoreOriginalLayers();
            base.OnNetworkDespawn();
        }

        private void CaptureOriginalLayers()
        {
            originalLayers = new int[characterRenderers.Length];

            for (int i = 0; i < characterRenderers.Length; i++)
            {
                Renderer targetRenderer = characterRenderers[i];
                originalLayers[i] = targetRenderer != null
                    ? targetRenderer.gameObject.layer
                    : 0;
            }
        }

        private void ApplyOwnerHiddenLayer()
        {
            int hiddenLayer = GetSingleLayerIndex(ownerHiddenLayer);
            if (hiddenLayer < 0)
            {
                Debug.LogError(
                    "[OwnerCharacterVisibility] Owner Hidden Layer must contain exactly one layer.",
                    this);
                return;
            }

            foreach (Renderer targetRenderer in characterRenderers)
            {
                if (targetRenderer != null)
                    targetRenderer.gameObject.layer = hiddenLayer;
            }

            ownerVisibilityApplied = true;
        }

        private void RestoreOriginalLayers()
        {
            if (!ownerVisibilityApplied || originalLayers == null)
                return;

            int count = Mathf.Min(characterRenderers.Length, originalLayers.Length);
            for (int i = 0; i < count; i++)
            {
                if (characterRenderers[i] != null)
                    characterRenderers[i].gameObject.layer = originalLayers[i];
            }

            ownerVisibilityApplied = false;
        }

        private static int GetSingleLayerIndex(LayerMask layerMask)
        {
            int value = layerMask.value;
            if (value == 0 || (value & (value - 1)) != 0)
                return -1;

            int layerIndex = 0;
            while ((value >>= 1) != 0)
                layerIndex++;

            return layerIndex;
        }
    }
}

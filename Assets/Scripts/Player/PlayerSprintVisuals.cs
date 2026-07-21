using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerSprintVisuals : MonoBehaviour
    {
        [Header("Edge Blur")]
        [Range(0f, 1f)] [SerializeField] private float sprintIntensity = 0.32f;
        [Range(0.05f, 0.95f)] [SerializeField] private float edgeStart = 0.44f;
        [Range(0f, 0.04f)] [SerializeField] private float blurRadius = 0.01f;

        [Header("Transition")]
        [Min(0.01f)] [SerializeField] private float fadeInDuration = 0.2f;
        [Min(0.01f)] [SerializeField] private float fadeOutDuration = 0.3f;

        private NetworkObject networkObject;
        private PlayerStamina stamina;
        private Volume runtimeVolume;
        private VolumeProfile runtimeProfile;
        private SprintEdgeBlur edgeBlur;
        private float targetIntensity;

        private bool IsLocalOwner => networkObject == null || !networkObject.IsSpawned || networkObject.IsOwner;

        private void Awake()
        {
            networkObject = GetComponent<NetworkObject>();
            stamina = GetComponent<PlayerStamina>();
        }

        private IEnumerator Start()
        {
            yield return null;
            if (!IsLocalOwner)
                yield break;

            CreateRuntimeVolume();
            if (stamina != null)
            {
                stamina.SprintStateChanged += OnSprintStateChanged;
                OnSprintStateChanged(stamina.IsSprinting);
            }
        }

        private void Update()
        {
            if (edgeBlur == null)
                return;

            float duration = targetIntensity > edgeBlur.intensity.value ? fadeInDuration : fadeOutDuration;
            float speed = sprintIntensity / Mathf.Max(0.01f, duration);
            edgeBlur.intensity.value = Mathf.MoveTowards(edgeBlur.intensity.value, targetIntensity, speed * Time.deltaTime);
        }

        private void OnDisable()
        {
            if (stamina != null)
                stamina.SprintStateChanged -= OnSprintStateChanged;

            if (edgeBlur != null)
                edgeBlur.intensity.value = 0f;
        }

        private void OnDestroy()
        {
            if (runtimeVolume != null)
                Destroy(runtimeVolume.gameObject);
            if (runtimeProfile != null)
                Destroy(runtimeProfile);
        }

        private void OnSprintStateChanged(bool isSprinting)
        {
            targetIntensity = isSprinting && IsLocalOwner ? sprintIntensity : 0f;
        }

        private void CreateRuntimeVolume()
        {
            GameObject volumeObject = new("Local Sprint Edge Blur", typeof(Volume));
            volumeObject.transform.SetParent(transform, false);

            runtimeVolume = volumeObject.GetComponent<Volume>();
            runtimeVolume.isGlobal = true;
            runtimeVolume.priority = 1000f;
            runtimeVolume.weight = 1f;

            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "Runtime Sprint Edge Blur";
            runtimeVolume.sharedProfile = runtimeProfile;

            edgeBlur = runtimeProfile.Add<SprintEdgeBlur>(true);
            edgeBlur.intensity.overrideState = true;
            edgeBlur.edgeStart.overrideState = true;
            edgeBlur.blurRadius.overrideState = true;
            edgeBlur.intensity.value = 0f;
            edgeBlur.edgeStart.value = edgeStart;
            edgeBlur.blurRadius.value = blurRadius;
        }
    }
}


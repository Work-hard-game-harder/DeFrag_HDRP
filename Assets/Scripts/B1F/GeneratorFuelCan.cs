using UnityEngine;

namespace DeFrag.B1F
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkWorldItem))]
    public sealed class GeneratorFuelCan : MonoBehaviour
    {
        [Header("Carry")]
        [SerializeField, Range(0.1f, 1f)] private float movementMultiplier = 0.5f;

        [Header("Continuous Search Signal")]
        [SerializeField] private Transform signalAnchor;
        [SerializeField] private AudioSource signalAudioSource;
        [SerializeField] private AudioClip signalClip;
        [SerializeField, Range(0f, 1f)] private float signalVolume = 1f;
        [Tooltip("Extra gain for a quiet ping recording. Reduce if the sound distorts.")]
        [SerializeField, Range(0f, 12f)] private float signalGainDb = 6f;
        [SerializeField, Min(1f)] private float signalMaxDistance = 80f;
        [SerializeField, Min(0.1f)] private float signalMinDistance = 8f;

        private NetworkWorldItem worldItem;

        public float MovementMultiplier => movementMultiplier;
        public Transform SignalAnchor => signalAnchor != null ? signalAnchor : transform;
        public NetworkWorldItem WorldItem => worldItem != null
            ? worldItem
            : worldItem = GetComponent<NetworkWorldItem>();

        private void Awake()
        {
            worldItem = GetComponent<NetworkWorldItem>();
            ConfigureAudioSource();
        }

        private void Reset()
        {
            signalAnchor = transform;
            signalAudioSource = GetComponent<AudioSource>();
            ConfigureAudioSource();
        }

        private void OnValidate()
        {
            movementMultiplier = Mathf.Clamp(movementMultiplier, 0.1f, 1f);
            signalMaxDistance = Mathf.Max(1f, signalMaxDistance);
            ConfigureAudioSource();
        }

        public void PlaySignal()
        {
            if (signalAudioSource != null && signalClip != null)
                signalAudioSource.PlayOneShot(signalClip,
                    signalVolume * Mathf.Pow(10f, signalGainDb / 20f));
        }

        public void StopSignal()
        {
            if (signalAudioSource != null)
                signalAudioSource.Stop();
        }

        private void Update()
        {
            if (worldItem != null && worldItem.State == NetworkItemState.Held &&
                signalAudioSource != null && signalAudioSource.isPlaying) StopSignal();
        }

        private void ConfigureAudioSource()
        {
            if (signalAudioSource == null)
                return;

            signalAudioSource.playOnAwake = false;
            signalAudioSource.loop = false;
            signalAudioSource.spatialBlend = 1f;
            signalAudioSource.volume = 1f;
            signalAudioSource.rolloffMode = AudioRolloffMode.Linear;
            signalAudioSource.minDistance = Mathf.Min(signalMinDistance, signalMaxDistance);
            signalAudioSource.maxDistance = signalMaxDistance;
        }
    }
}

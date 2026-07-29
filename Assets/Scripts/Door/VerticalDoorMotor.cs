using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace DeFrag.Doors
{
    public sealed class VerticalDoorMotor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float openHeight = 6f;
        [SerializeField, Min(0.01f)] private float moveSpeed = 3f;

        [Header("Door Sounds")]
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;
        [SerializeField, Range(0f, 1f)] private float soundVolume = 1f;
        [SerializeField, Min(0f)] private float minDistance = 2f;
        [SerializeField, Min(0.01f)] private float maxDistance = 20f;
        [SerializeField] private AudioMixerGroup outputMixerGroup;

        private Vector3 closedPosition;
        private Vector3 openPosition;
        private AudioSource audioSource;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            closedPosition = transform.position;
            openPosition = closedPosition + Vector3.up * openHeight;

            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;
            audioSource.dopplerLevel = 0f;
            audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            audioSource.minDistance = minDistance;
            audioSource.maxDistance = maxDistance;
            audioSource.outputAudioMixerGroup = outputMixerGroup;
        }

        public void Open()
        {
            if (IsOpen)
                return;

            IsOpen = true;
            PlayDoorSound(openSound);
            MoveTo(openPosition);
        }

        public void Close()
        {
            if (!IsOpen)
                return;

            IsOpen = false;
            PlayDoorSound(closeSound);
            MoveTo(closedPosition);
        }

        private void PlayDoorSound(AudioClip clip)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.volume = soundVolume;

            if (clip != null)
                audioSource.Play();
        }

        private void MoveTo(Vector3 target)
        {
            StopAllCoroutines();
            StartCoroutine(MoveRoutine(target));
        }

        private IEnumerator MoveRoutine(Vector3 target)
        {
            while (transform.position != target)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target,
                    moveSpeed * Time.deltaTime);
                yield return null;
            }
        }
    }
}

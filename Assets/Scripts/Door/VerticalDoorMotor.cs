using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace DeFrag.Doors
{
    public sealed class VerticalDoorMotor : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float openHeight = 6f;
        [SerializeField, Min(0.01f)] private float moveSpeed = 3f;
        [SerializeField, Min(0.01f)] private float forcedCloseSpeed = 12f;

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
        private Coroutine movementRoutine;
        private Coroutine timedLockRoutine;

        public bool IsOpen { get; private set; }
        public bool IsTemporarilyLocked { get; private set; }
        public bool IsAccessLocked { get; private set; }
        public bool IsLocked => IsAccessLocked || IsTemporarilyLocked;

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
            if (IsLocked || IsOpen)
                return;

            IsOpen = true;
            PlayDoorSound(openSound);
            MoveTo(openPosition);
        }

        public void Close()
        {
            if (!IsOpen && transform.position == closedPosition)
                return;

            IsOpen = false;
            PlayDoorSound(closeSound);
            MoveTo(closedPosition, moveSpeed);
        }

        public bool LockClosed(float duration)
        {
            if (IsTemporarilyLocked)
                return false;

            IsTemporarilyLocked = true;
            IsOpen = false;
            PlayDoorSound(closeSound);
            MoveTo(closedPosition, forcedCloseSpeed);
            timedLockRoutine = StartCoroutine(TimedLockRoutine(duration));
            return true;
        }

        public void SetAccessLocked(bool locked)
        {
            IsAccessLocked = locked;

            if (locked && IsOpen)
                Close();
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
            MoveTo(target, moveSpeed);
        }

        private void MoveTo(Vector3 target, float speed)
        {
            if (movementRoutine != null)
                StopCoroutine(movementRoutine);

            movementRoutine = StartCoroutine(MoveRoutine(target, speed));
        }

        private IEnumerator MoveRoutine(Vector3 target, float speed)
        {
            while (transform.position != target)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    target,
                    speed * Time.deltaTime);
                yield return null;
            }

            movementRoutine = null;
        }

        private IEnumerator TimedLockRoutine(float duration)
        {
            while (transform.position != closedPosition)
                yield return null;

            yield return new WaitForSeconds(Mathf.Max(0f, duration));
            IsTemporarilyLocked = false;
            timedLockRoutine = null;
        }
    }
}

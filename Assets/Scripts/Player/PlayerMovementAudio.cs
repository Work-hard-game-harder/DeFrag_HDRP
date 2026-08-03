using System.Collections;
using EasyPeasyFirstPersonController;
using Unity.Netcode;
using UnityEngine;

namespace DeFrag.Player
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerStamina))]
    public sealed class PlayerMovementAudio : NetworkBehaviour
    {
        [Header("Walking Footsteps")]
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private AudioClip[] walkClips;
        [Range(0f, 1f)] [SerializeField] private float walkVolume = 0.75f;
        [SerializeField] private Vector2 walkPitchRange = new(0.95f, 1.05f);

        [Header("Running Footsteps")]
        [SerializeField] private AudioClip[] runClips;
        [Range(0f, 1f)] [SerializeField] private float runVolume = 0.9f;
        [SerializeField] private Vector2 runPitchRange = new(0.95f, 1.05f);

        [Header("Jump")]
        [SerializeField] private AudioClip[] jumpClips;
        [Range(0f, 1f)] [SerializeField] private float jumpVolume = 0.85f;
        [SerializeField] private Vector2 jumpPitchRange = new(0.97f, 1.03f);

        [Header("Exhaustion Breathing")]
        [SerializeField] private AudioSource breathingSource;
        [SerializeField] private AudioClip[] exhaustionClips;
        [Range(0f, 1f)] [SerializeField] private float exhaustionVolume = 1f;
        [Min(0.01f)] [SerializeField] private float exhaustionMaximumDuration = 5f;

        private PlayerStamina stamina;
        private FirstPersonController movement;
        private Coroutine exhaustionStopRoutine;

        private void Awake()
        {
            stamina = GetComponent<PlayerStamina>();
            movement = GetComponent<FirstPersonController>();
            EnsureAudioSources();
        }

        private void OnEnable()
        {
            stamina.Exhausted += PlayExhaustionBreathing;
        }

        private void OnDisable()
        {
            stamina.Exhausted -= PlayExhaustionBreathing;
            if (exhaustionStopRoutine != null)
            {
                StopCoroutine(exhaustionStopRoutine);
                exhaustionStopRoutine = null;
            }
            breathingSource.Stop();
        }

        public void PlayFootstep()
        {
            if (!movement.isGrounded)
                return;

            bool running = stamina.IsSprinting;
            AudioClip[] clips = running ? runClips : walkClips;
            if (clips.Length == 0)
                return;

            int clipIndex = Random.Range(0, clips.Length);
            Vector2 pitchRange = running ? runPitchRange : walkPitchRange;
            float pitch = Random.Range(pitchRange.x, pitchRange.y);

            if (!IsSpawned)
            {
                PlayFootstepLocal(running, clipIndex, pitch);
                return;
            }

            if (IsOwner)
                RequestFootstepServerRpc(running, clipIndex, pitch);
        }

        [ServerRpc]
        private void RequestFootstepServerRpc(bool running, int clipIndex, float pitch)
        {
            PlayFootstepClientRpc(running, clipIndex, pitch);
        }

        [ClientRpc]
        private void PlayFootstepClientRpc(bool running, int clipIndex, float pitch)
        {
            PlayFootstepLocal(running, clipIndex, pitch);
        }

        private void PlayFootstepLocal(bool running, int clipIndex, float pitch)
        {
            AudioClip[] clips = running ? runClips : walkClips;
            footstepSource.pitch = pitch;
            footstepSource.PlayOneShot(
                clips[clipIndex],
                running ? runVolume : walkVolume);
        }

        public void PlayJump()
        {
            if (jumpClips.Length == 0)
                return;

            int clipIndex = Random.Range(0, jumpClips.Length);
            float pitch = Random.Range(jumpPitchRange.x, jumpPitchRange.y);
            if (!IsSpawned)
            {
                PlayJumpLocal(clipIndex, pitch);
                return;
            }

            if (IsOwner)
                RequestJumpServerRpc(clipIndex, pitch);
        }

        [ServerRpc]
        private void RequestJumpServerRpc(int clipIndex, float pitch)
        {
            PlayJumpClientRpc(clipIndex, pitch);
        }

        [ClientRpc]
        private void PlayJumpClientRpc(int clipIndex, float pitch)
        {
            PlayJumpLocal(clipIndex, pitch);
        }

        private void PlayJumpLocal(int clipIndex, float pitch)
        {
            footstepSource.pitch = pitch;
            footstepSource.PlayOneShot(jumpClips[clipIndex], jumpVolume);
        }

        private void PlayExhaustionBreathing()
        {
            if ((IsSpawned && !IsOwner) || exhaustionClips.Length == 0)
                return;

            AudioClip clip = exhaustionClips[Random.Range(0, exhaustionClips.Length)];
            if (exhaustionStopRoutine != null)
                StopCoroutine(exhaustionStopRoutine);
            breathingSource.Stop();
            breathingSource.pitch = 1f;
            breathingSource.PlayOneShot(clip, exhaustionVolume);
            exhaustionStopRoutine = StartCoroutine(StopExhaustionAfterDuration());
        }

        private IEnumerator StopExhaustionAfterDuration()
        {
            yield return new WaitForSeconds(exhaustionMaximumDuration);
            breathingSource.Stop();
            exhaustionStopRoutine = null;
        }

        private void OnValidate()
        {
            if (walkPitchRange.x > walkPitchRange.y)
                walkPitchRange = new Vector2(walkPitchRange.y, walkPitchRange.x);
            if (runPitchRange.x > runPitchRange.y)
                runPitchRange = new Vector2(runPitchRange.y, runPitchRange.x);
            if (jumpPitchRange.x > jumpPitchRange.y)
                jumpPitchRange = new Vector2(jumpPitchRange.y, jumpPitchRange.x);
            exhaustionMaximumDuration = Mathf.Max(0.01f, exhaustionMaximumDuration);
        }

        private void EnsureAudioSources()
        {
            if (footstepSource == null)
                footstepSource = gameObject.AddComponent<AudioSource>();
            footstepSource.playOnAwake = false;
            footstepSource.loop = false;
            footstepSource.spatialBlend = 1f;
            footstepSource.minDistance = 1f;
            footstepSource.maxDistance = 16f;
            footstepSource.dopplerLevel = 0f;

            if (breathingSource == null)
                breathingSource = gameObject.AddComponent<AudioSource>();
            breathingSource.playOnAwake = false;
            breathingSource.loop = false;
            breathingSource.spatialBlend = 0f;
            breathingSource.dopplerLevel = 0f;
        }
    }
}

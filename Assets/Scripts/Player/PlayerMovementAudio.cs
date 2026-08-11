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

        [Header("Person Controller Automatic Events")]
        [SerializeField, Min(0.01f)] private float movementSpeedThreshold = 0.15f;
        [SerializeField, Min(0.05f)] private float walkStepInterval = 0.48f;
        [SerializeField, Min(0.05f)] private float runStepInterval = 0.32f;
        [SerializeField, Min(0f)] private float jumpVelocityThreshold = 0.5f;

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
        private StarterAssets.PersonController movement;
        private CharacterController characterController;
        private Coroutine exhaustionStopRoutine;
        private float footstepTimer;
        private bool wasGrounded;

        private void Awake()
        {
            stamina = GetComponent<PlayerStamina>();
            movement = GetComponent<StarterAssets.PersonController>();
            characterController = GetComponent<CharacterController>();
            EnsureAudioSources();
            wasGrounded = movement != null && movement.Grounded;
        }

        private void Update()
        {
            // EasyPeasy FirstPersonController invokes PlayFootstep/PlayJump itself.
            // The network PlayerCharacter(H) uses PersonController, which exposes no
            // animation events, so generate those events from its actual velocity.
            if (movement == null || characterController == null ||
                (IsSpawned && !IsOwner))
                return;

            bool grounded = movement.Grounded;
            Vector3 velocity = characterController.velocity;
            float horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;

            if (grounded && horizontalSpeed >= movementSpeedThreshold)
            {
                bool running = horizontalSpeed > (movement.MoveSpeed + movement.SprintSpeed) * 0.5f;
                footstepTimer += Time.deltaTime;
                float interval = running ? runStepInterval : walkStepInterval;
                if (footstepTimer >= interval)
                {
                    footstepTimer %= interval;
                    TriggerFootstep(running);
                }
            }
            else
            {
                footstepTimer = 0f;
            }

            if (wasGrounded && !grounded && velocity.y > jumpVelocityThreshold)
                PlayJump();
            wasGrounded = grounded;
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
            if (movement == null || !movement.Grounded)
                return;

            TriggerFootstep(stamina.IsSprinting);
        }

        private void TriggerFootstep(bool running)
        {
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
            movementSpeedThreshold = Mathf.Max(0.01f, movementSpeedThreshold);
            walkStepInterval = Mathf.Max(0.05f, walkStepInterval);
            runStepInterval = Mathf.Max(0.05f, runStepInterval);
            jumpVelocityThreshold = Mathf.Max(0f, jumpVelocityThreshold);
            exhaustionMaximumDuration = Mathf.Max(0.01f, exhaustionMaximumDuration);
        }

        private void EnsureAudioSources()
        {
            if (footstepSource == null)
                footstepSource = CreateChildAudioSource("Footstep Audio");
            footstepSource.playOnAwake = false;
            footstepSource.loop = false;
            footstepSource.spatialBlend = 1f;
            footstepSource.minDistance = 1f;
            footstepSource.maxDistance = 16f;
            footstepSource.dopplerLevel = 0f;

            if (breathingSource == null)
                breathingSource = CreateChildAudioSource("Breathing Audio");
            breathingSource.playOnAwake = false;
            breathingSource.loop = false;
            breathingSource.spatialBlend = 0f;
            breathingSource.dopplerLevel = 0f;
        }

        private AudioSource CreateChildAudioSource(string childName)
        {
            Transform child = transform.Find(childName);
            if (child == null)
            {
                GameObject childObject = new(childName);
                child = childObject.transform;
                child.SetParent(transform, false);
            }

            AudioSource source = child.GetComponent<AudioSource>();
            return source != null ? source : child.gameObject.AddComponent<AudioSource>();
        }
    }
}

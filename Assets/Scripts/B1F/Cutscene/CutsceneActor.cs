using System.Collections.Generic;
using UnityEngine;

namespace DeFrag.B1F
{
    /// <summary>
    /// A local, non-networked puppet for cutscenes. Drives a humanoid through either the player
    /// locomotion parameters (Speed/MotionSpeed) or named animator states, walks waypoint paths,
    /// and layers procedural head-look, flinch and knock-back on top of the animation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CutsceneActor : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int MotionSpeedId = Animator.StringToHash("MotionSpeed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int CrouchId = Animator.StringToHash("IsCrouching");

        private Animator animator;
        private bool parameterLocomotion;
        private bool hasCrouch;
        private string walkState = "Walking";
        private string idleState = "Idle";
        private float nominalWalkSpeed = 1.4f;

        private readonly List<(Transform bone, float weight)> lookChain = new();
        private Transform hips;
        private Vector3 lookTarget;
        private float lookWeight;
        private float lookWeightGoal;
        private float lookBlend = 6f;
        private float maxYaw = 85f;
        private float maxPitch = 35f;

        private readonly Queue<Vector3> path = new();
        private float moveSpeed;
        private float animSpeed;
        private float motionSpeed = 1f;
        private bool faceTravel = true;
        private float yawGoal;
        private float turnRate;
        private bool moving;
        private string currentState;

        private float flinchTime = -10f;
        private float flinchStrength;
        private Vector3 flinchFrom;
        private Vector3 shoveVelocity;

        public Animator Animator => animator;
        public Transform Head => animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : transform;
        public bool IsMoving => moving;

        public static CutsceneActor Spawn(GameObject model, RuntimeAnimatorController controller, Transform parent,
            Vector3 position, float yaw, Material[] materials = null, float scale = 1f)
        {
            GameObject instance = Instantiate(model, position, Quaternion.Euler(0f, yaw, 0f), parent);
            instance.name = $"{model.name} (Cutscene Actor)";
            instance.transform.localScale *= scale;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) Destroy(collider);
            if (materials != null && materials.Length > 0)
                foreach (SkinnedMeshRenderer skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.sharedMaterials = materials;
            var actor = instance.AddComponent<CutsceneActor>();
            actor.Initialise(controller, yaw);
            return actor;
        }

        private void Initialise(RuntimeAnimatorController controller, float yaw)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null) animator = gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            if (animator.gameObject != gameObject) animator.gameObject.AddComponent<CutsceneAnimationEventSink>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            yawGoal = yaw;
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.nameHash == SpeedId) parameterLocomotion = true;
                if (parameter.nameHash == CrouchId) hasCrouch = true;
            }
            if (parameterLocomotion)
            {
                animator.SetBool(GroundedId, true);
                animator.SetFloat(MotionSpeedId, 1f);
            }

            if (animator.isHuman)
            {
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                AddLookBone(HumanBodyBones.Spine, 0.14f);
                AddLookBone(HumanBodyBones.Chest, 0.18f);
                AddLookBone(HumanBodyBones.UpperChest, 0.12f);
                AddLookBone(HumanBodyBones.Neck, 0.22f);
                AddLookBone(HumanBodyBones.Head, 0.34f);
                float total = 0f;
                foreach (var entry in lookChain) total += entry.weight;
                for (int i = 0; i < lookChain.Count; i++) lookChain[i] = (lookChain[i].bone, lookChain[i].weight / total);
            }
        }

        private void AddLookBone(HumanBodyBones id, float weight)
        {
            Transform bone = animator.GetBoneTransform(id);
            if (bone != null) lookChain.Add((bone, weight));
        }

        /// <summary>For actors without locomotion parameters: which states to cross-fade.</summary>
        public void UseStates(string walk, string idle, float nominalSpeed)
        {
            walkState = walk;
            idleState = idle;
            nominalWalkSpeed = Mathf.Max(0.1f, nominalSpeed);
            PlayState(idleState, 0f);
        }

        public void PlayState(string state, float fade = 0.25f)
        {
            if (animator == null || parameterLocomotion || string.IsNullOrEmpty(state) || state == currentState) return;
            currentState = state;
            animator.CrossFadeInFixedTime(state, fade);
        }

        public void LookAt(Vector3 target, float weight = 1f, float blend = 6f)
        {
            lookTarget = target;
            lookWeightGoal = Mathf.Clamp01(weight);
            lookBlend = blend;
        }

        public void SetLookLimits(float yaw, float pitch)
        {
            maxYaw = yaw;
            maxPitch = pitch;
        }

        public void ClearLook(float blend = 4f)
        {
            lookWeightGoal = 0f;
            lookBlend = blend;
        }

        public void FaceYaw(float yaw, float degreesPerSecond)
        {
            yawGoal = yaw;
            turnRate = degreesPerSecond;
        }

        public void FacePoint(Vector3 point, float degreesPerSecond)
        {
            Vector3 flat = point - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.0001f) FaceYaw(Quaternion.LookRotation(flat).eulerAngles.y, degreesPerSecond);
        }

        /// <summary>Walk through the given points at a constant speed.</summary>
        public void Walk(IEnumerable<Vector3> points, float speed, float blendSpeed = 1.2f, float playback = 0.8f, bool faceDirection = true)
        {
            path.Clear();
            foreach (Vector3 point in points) path.Enqueue(point);
            moveSpeed = speed;
            animSpeed = blendSpeed;
            motionSpeed = playback;
            faceTravel = faceDirection;
            moving = path.Count > 0;
            if (!parameterLocomotion && moving)
            {
                PlayState(walkState);
                animator.speed = Mathf.Clamp(speed / nominalWalkSpeed, 0.5f, 2f);
            }
        }

        public void Halt()
        {
            path.Clear();
            moving = false;
            if (!parameterLocomotion)
            {
                PlayState(idleState);
                if (animator != null) animator.speed = 1f;
            }
        }

        public void SetCrouch(bool crouch)
        {
            if (hasCrouch) animator.SetBool(CrouchId, crouch);
        }

        public void Flinch(Vector3 source, float strength)
        {
            flinchTime = Time.time;
            flinchStrength = strength;
            flinchFrom = source;
        }

        /// <summary>Instant push away from a point, decaying quickly (no animation change).</summary>
        public void Shove(Vector3 source, float speed)
        {
            Vector3 away = transform.position - source;
            away.y = 0f;
            shoveVelocity = away.normalized * speed;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float planarSpeed = 0f;
            if (moving && path.Count > 0)
            {
                Vector3 next = path.Peek();
                Vector3 to = next - transform.position;
                to.y = 0f;
                float step = moveSpeed * dt;
                if (to.magnitude <= step)
                {
                    transform.position = new Vector3(next.x, transform.position.y, next.z);
                    path.Dequeue();
                    if (path.Count == 0) Halt();
                }
                else
                {
                    transform.position += to.normalized * step;
                    if (faceTravel) FaceYaw(Quaternion.LookRotation(to).eulerAngles.y, 220f);
                }
                planarSpeed = moving ? animSpeed : 0f;
            }

            if (shoveVelocity.sqrMagnitude > 0.0001f)
            {
                transform.position += shoveVelocity * dt;
                shoveVelocity = Vector3.MoveTowards(shoveVelocity, Vector3.zero, 9f * dt);
            }

            float yaw = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(yaw, yawGoal);
            bool turning = Mathf.Abs(delta) > 2f;
            if (turning)
                transform.rotation = Quaternion.Euler(0f, Mathf.MoveTowardsAngle(yaw, yawGoal, turnRate * dt), 0f);

            if (parameterLocomotion)
            {
                // Turning on the spot reads as small shuffling steps rather than a sliding statue.
                float target = moving ? planarSpeed : turning ? 0.55f : 0f;
                animator.SetFloat(SpeedId, target, 0.15f, dt);
                animator.SetFloat(MotionSpeedId, moving ? motionSpeed : 1f);
            }

            lookWeight = Mathf.MoveTowards(lookWeight, lookWeightGoal, lookBlend * dt);
        }

        private void LateUpdate()
        {
            if (lookChain.Count == 0) return;
            Transform head = lookChain[lookChain.Count - 1].bone;

            float yaw = 0f, pitch = 0f;
            if (lookWeight > 0.001f)
            {
                Vector3 local = Quaternion.Inverse(Quaternion.Euler(0f, transform.eulerAngles.y, 0f)) * (lookTarget - head.position);
                yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxYaw, maxYaw) * lookWeight;
                float flat = new Vector2(local.x, local.z).magnitude;
                pitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -maxPitch, maxPitch) * lookWeight;
            }

            // Flinch: a sharp recoil away from the source that settles within half a second.
            float age = Time.time - flinchTime;
            float recoil = age < 0.6f ? flinchStrength * Mathf.Exp(-age * 7f) * Mathf.Sin(Mathf.Min(1f, age * 14f) * Mathf.PI * 0.5f) : 0f;
            float recoilPitch = -14f * recoil;
            if (recoil > 0.001f)
            {
                Vector3 local = transform.InverseTransformPoint(flinchFrom);
                yaw += Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -60f, 60f) * 0.35f * recoil;
                if (hips != null) hips.position += Vector3.up * 0.05f * recoil;
            }

            Vector3 right = transform.right;
            foreach (var (bone, weight) in lookChain)
                bone.rotation = Quaternion.AngleAxis(yaw * weight, Vector3.up) *
                                Quaternion.AngleAxis((pitch + recoilPitch) * weight, right) * bone.rotation;
        }

        // Starter Assets clips carry footstep/landing events; the puppet simply accepts them.
        private void OnFootstep(AnimationEvent animationEvent) { }
        private void OnLand(AnimationEvent animationEvent) { }
    }

    /// <summary>Accepts animation events when the Animator lives below the actor root.</summary>
    public sealed class CutsceneAnimationEventSink : MonoBehaviour
    {
        private void OnFootstep(AnimationEvent animationEvent) { }
        private void OnLand(AnimationEvent animationEvent) { }
    }
}

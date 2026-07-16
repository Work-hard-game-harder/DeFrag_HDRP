namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerWakieTakieState : PlayerBaseState
    {
        private SoundEmitter soundEmitter;
        private MicVolumeUI micVolumeUI;
        public PlayerWakieTakieState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        
        public override void EnterState()
        {
            if (ctx.wakieTakie != null)
            {
                ctx.wakieTakie.SetActive(true);

                if (ctx.wakieTakieAnimator == null)
                    ctx.wakieTakieAnimator = ctx.wakieTakie.GetComponentInChildren<Animator>(true); // includeInactive: true

                soundEmitter = ctx.GetComponentInChildren<SoundEmitter>();

                micVolumeUI = Object.FindAnyObjectByType<MicVolumeUI>();
                micVolumeUI?.ShowUI();

            }
        }


        public override void UpdateState()
        {
            if (soundEmitter != null)
            {
                // 마우스 클릭 시 마이크 켜기/끄기
                if (Input.GetMouseButtonDown(0))
                    soundEmitter.StartMic();
                else if (Input.GetMouseButtonUp(0))
                    soundEmitter.StopMic();
            }

            if (ctx.wakieTakieAnimator != null)
            {
                if (Input.GetMouseButton(0))
                {
                    ctx.wakieTakieAnimator.SetFloat("speed", 1f);
                    ctx.wakieTakieAnimator.SetBool("isTalking", true);
                }
                else
                {
                    ctx.wakieTakieAnimator.SetFloat("speed", -1f);
                    ctx.wakieTakieAnimator.SetBool("isTalking", false);
                }
            }

            ctx.targetCameraY = ctx.standingCameraHeight;
            bool isSprinting = ctx.input.sprint && ctx.input.moveInput.y > 0;
            float speed = isSprinting ? ctx.sprintSpeed : ctx.walkSpeed;
            ctx.targetFov = isSprinting ? ctx.sprintFov : ctx.normalFov;
            ctx.currentBobIntensity = ctx.bobAmount * (isSprinting ? 1.5f : 1f);
            ctx.currentBobSpeed = ctx.bobSpeed * (isSprinting ? 1.3f : 1f);
            ctx.targetTilt = 0;

            Vector2 input = ctx.input.moveInput;
            Vector3 move = ctx.transform.right * input.x + ctx.transform.forward * input.y;
            Vector3 finalVelocity = move * speed;
            finalVelocity.y = -20f;
            ctx.characterController.Move(finalVelocity * Time.deltaTime);
        }


        public override void ExitState()
        {
            if (soundEmitter != null)
                soundEmitter.StopMic();

            if (ctx.wakieTakie != null)
                ctx.wakieTakie.SetActive(false);
        }

        public override void CheckSwitchStates() { }
    }
}
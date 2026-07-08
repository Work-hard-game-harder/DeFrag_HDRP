namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerWakieTakieState : PlayerBaseState
    {
        public PlayerWakieTakieState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            if (ctx.wakieTakie != null)
                ctx.wakieTakie.SetActive(true);
        }


        public override void UpdateState()
        {
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
            if (ctx.wakieTakie != null)
                ctx.wakieTakie.SetActive(false);
        }

        public override void CheckSwitchStates() { }
    }
}
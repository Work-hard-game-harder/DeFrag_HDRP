namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerSubtitleState : PlayerBaseState
    {
        public PlayerSubtitleState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            // 이동 멈추기
            ctx.moveDirection = Vector3.zero;
        }

        public override void UpdateState()
        {
            // 자막이 끝나면 Grounded로 복귀
            if (!GameState.isCutscene)
            {
                SwitchState(factory.Grounded());
            }
        }

        public override void ExitState() { }

        public override void CheckSwitchStates() { }
    }
}
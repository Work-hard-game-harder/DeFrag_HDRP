namespace EasyPeasyFirstPersonController
{
    public class PlayerHidingState : PlayerBaseState
    {
        public PlayerHidingState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            ctx.IsHiding = true;
        }

        public override void UpdateState()
        {
            // 숨기 해제 조건 (예: 다시 일어서면)
            if (!ctx.isCrouching)
            {
                SwitchState(factory.Grounded());
            }
        }

        public override void ExitState()
        {
            ctx.IsHiding = false;
        }

        public override void CheckSwitchStates() { }
    }
}
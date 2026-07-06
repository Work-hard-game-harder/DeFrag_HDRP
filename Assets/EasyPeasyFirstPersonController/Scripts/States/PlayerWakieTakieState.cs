namespace EasyPeasyFirstPersonController
{
    using UnityEngine;

    public class PlayerWakieTakieState : PlayerBaseState
    {
        public PlayerWakieTakieState(FirstPersonController currentContext, PlayerStateFactory playerStateFactory)
            : base(currentContext, playerStateFactory) { }

        public override void EnterState()
        {
            // 워키토키 활성화
            if (ctx.wakieTakie != null)
                ctx.wakieTakie.SetActive(true);
        }

        public override void UpdateState()
        {
            // 기존 이동 상태 업데이트 유지 (이동 제한 없음)
            if (Input.GetKeyDown(KeyCode.G))
            {
                if (ctx.wakieTakie != null)
                    ctx.wakieTakie.SetActive(false);
                SwitchState(factory.Grounded());
            }
        }

        public override void ExitState() { }
        public override void CheckSwitchStates() { }
    }
}
using BehaviorDesigner.Runtime.Tasks;

namespace DeFrag.Monsters.BehaviorDesignerTasks
{
    [TaskCategory("DeFrag/Monster")]
    [TaskDescription("현재 MonsterAI 상태가 지정 상태이면 해당 상태 로직을 한 프레임 실행합니다.")]
    public sealed class RunMonsterState : Action
    {
        public MonsterAI.MonsterState state;

        private MonsterAI monsterAI;

        public override void OnAwake()
        {
            monsterAI = GetComponent<MonsterAI>();
        }

        public override TaskStatus OnUpdate()
        {
            if (monsterAI == null || !monsterAI.HasSimulationAuthority)
                return TaskStatus.Failure;

            return monsterAI.TickBehaviorState(state)
                ? TaskStatus.Running
                : TaskStatus.Failure;
        }

        public override void OnReset()
        {
            state = MonsterAI.MonsterState.Search;
        }
    }
}

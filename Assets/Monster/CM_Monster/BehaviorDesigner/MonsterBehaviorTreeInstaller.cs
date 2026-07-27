using System.Collections.Generic;
using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using DeFrag.Monsters.BehaviorDesignerTasks;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterAI))]
[DefaultExecutionOrder(10000)]
public sealed class MonsterBehaviorTreeInstaller : MonoBehaviour
{
    private void Awake()
    {
        EnsureBehaviorTree();
    }

    public BehaviorTree EnsureBehaviorTree()
    {
        MonsterAI monsterAI = GetComponent<MonsterAI>();
        if (monsterAI == null || !monsterAI.UsesBehaviorDesigner)
            return null;

        BehaviorTree behaviorTree = GetComponent<BehaviorTree>();
        if (behaviorTree == null)
            behaviorTree = gameObject.AddComponent<BehaviorTree>();

        BehaviorSource source = behaviorTree.GetBehaviorSource();
        if (!HasValidGeneratedTree(source))
        {
            BuildRuntimeTree(source);
        }

        return behaviorTree;
    }

    private static bool HasValidGeneratedTree(BehaviorSource source)
    {
        if (!(source.RootTask is SelectorEvaluator selector) || selector.ID != 1 ||
            selector.FriendlyName != "B1F Monster State Selector" ||
            selector.Children == null || selector.Children.Count != 6)
            return false;

        for (int i = 0; i < selector.Children.Count; i++)
        {
            if (!(selector.Children[i] is RunMonsterState) || selector.Children[i].ID != i + 2)
                return false;
        }

        return source.EntryTask is EntryTask entry &&
               entry.ID == 0 &&
               (entry.Children == null || entry.Children.Count == 0) &&
               (source.DetachedTasks == null || source.DetachedTasks.Count == 0);
    }

    private static void BuildRuntimeTree(BehaviorSource source)
    {
        var selector = new SelectorEvaluator
        {
            ID = 1,
            FriendlyName = "B1F Monster State Selector",
            NodeData = new NodeData { Offset = new Vector2(0f, 120f) }
        };
        selector.AddChild(CreateStateTask(2, MonsterAI.MonsterState.Missing, -500f), 0);
        selector.AddChild(CreateStateTask(3, MonsterAI.MonsterState.Attack, -300f), 1);
        selector.AddChild(CreateStateTask(4, MonsterAI.MonsterState.Chase, -100f), 2);
        selector.AddChild(CreateStateTask(5, MonsterAI.MonsterState.Investigate, 100f), 3);
        selector.AddChild(CreateStateTask(6, MonsterAI.MonsterState.Search, 300f), 4);
        selector.AddChild(CreateStateTask(7, MonsterAI.MonsterState.Idle, 500f), 5);

        var entry = new EntryTask
        {
            ID = 0,
            FriendlyName = "Entry",
            NodeData = new NodeData { Offset = Vector2.zero }
        };
        source.EntryTask = entry;
        source.RootTask = selector;
        source.DetachedTasks = new List<Task>();
    }

    private static RunMonsterState CreateStateTask(int id, MonsterAI.MonsterState state, float horizontalOffset)
    {
        return new RunMonsterState
        {
            ID = id,
            state = state,
            FriendlyName = state.ToString(),
            NodeData = new NodeData { Offset = new Vector2(horizontalOffset, 260f) }
        };
    }
}

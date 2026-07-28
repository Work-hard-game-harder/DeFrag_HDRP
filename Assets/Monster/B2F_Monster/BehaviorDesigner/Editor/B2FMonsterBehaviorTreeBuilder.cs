#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using DeFrag.Monsters.B2F.BehaviorDesignerTasks;
using DeFrag.Monsters.Common.BehaviorDesignerTasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Action = BehaviorDesigner.Runtime.Tasks.Action;

namespace DeFrag.Monsters.B2F.Editor
{
    public static class B2FMonsterBehaviorTreeBuilder
    {
        private const string MenuPath = "Tools/Behavior Designer/B2F Monster/Create Tree For Selected Monster";

        [MenuItem(MenuPath)]
        public static void CreateForSelectedMonster()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("B2F Monster", "Hierarchy에서 몬스터 오브젝트를 선택해 주세요.", "확인");
                return;
            }

            Build(selected, true);
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateCreateForSelectedMonster()
        {
            return Selection.activeGameObject != null;
        }

        public static BehaviorTree Build(GameObject monster, bool recordUndo)
        {
            if (monster == null)
                return null;

            if (recordUndo)
                Undo.RegisterFullObjectHierarchyUndo(monster, "Create B2F Monster Behavior Tree");

            NavMeshAgent agent = monster.GetComponent<NavMeshAgent>();
            if (agent == null)
                agent = recordUndo ? Undo.AddComponent<NavMeshAgent>(monster) : monster.AddComponent<NavMeshAgent>();

            AudioSource audioSource = monster.GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = recordUndo ? Undo.AddComponent<AudioSource>(monster) : monster.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            BehaviorTree behaviorTree = monster.GetComponent<BehaviorTree>();
            if (behaviorTree == null)
                behaviorTree = recordUndo ? Undo.AddComponent<BehaviorTree>(monster) : monster.AddComponent<BehaviorTree>();

            var serializedTree = new SerializedObject(behaviorTree);
            SerializedProperty restartWhenComplete = serializedTree.FindProperty("restartWhenComplete");
            if (restartWhenComplete != null)
            {
                restartWhenComplete.boolValue = true;
                serializedTree.ApplyModifiedPropertiesWithoutUndo();
            }

            BehaviorSource source = behaviorTree.GetBehaviorSource();
            source.SetAllVariables(CreateVariables(source));

            SharedTransform playerTarget = Shared<SharedTransform>("PlayerTarget");
            SharedFloat attackRange = Shared<SharedFloat>("attackRange");
            SharedFloat mimicIntervalMin = Shared<SharedFloat>("mimicIntervalMin");
            SharedFloat mimicIntervalMax = Shared<SharedFloat>("mimicIntervalMax");
            SharedAudioClipList mimicClips = Shared<SharedAudioClipList>("MimicVoiceClips");
            SharedVector3 patrolDestination = Shared<SharedVector3>("PatrolDestination");
            SharedFloat patrolRadius = Shared<SharedFloat>("patrolRadius");
            SharedFloat walkSpeed = Shared<SharedFloat>("walkSpeed");
            SharedFloat idleTimeMin = Shared<SharedFloat>("idleTimeMin");
            SharedFloat idleTimeMax = Shared<SharedFloat>("idleTimeMax");

            var root = Node(new Selector(), 1, "B2F Monster StateSelector", 0f, 110f);
            SetAbortType(root, AbortType.LowerPriority);

            var attack = Node(new Sequence(), 2, "Attack", -360f, 260f);
            SetAbortType(attack, AbortType.LowerPriority);
            attack.AddChild(Node(new WithinTargetDistance
            {
                target = playerTarget,
                distance = attackRange
            }, 3, "Within Attack Range", -430f, 400f), 0);
            attack.AddChild(Node(new AttackPlayer
            {
                playerTarget = playerTarget,
                damageRange = attackRange
            }, 4, "Attack Player", -290f, 400f), 1);

            var chase = Node(new Sequence(), 5, "Chase", 0f, 260f);
            SetAbortType(chase, AbortType.Both);
            chase.AddChild(Node(new CanSeeTarget
            {
                target = playerTarget
            }, 6, "Can See Player", -70f, 400f), 0);
            chase.AddChild(Node(new ChaseTargetWithDetour
            {
                target = playerTarget,
                stoppingDistance = attackRange
            }, 7, "Chase With Detour", 70f, 400f), 1);

            var patrol = Node(new Sequence(), 8, "Patrol", 360f, 260f);
            var parallel = Node(new Parallel(), 9, "Patrol And Voice Mimic", 360f, 400f);

            var patrolRepeater = Node(new Repeater
            {
                repeatForever = true,
                endOnFailure = false
            }, 10, "Random Patrol Loop", 230f, 540f);
            var patrolCycle = Node(new Sequence(), 11, "Random Patrol Cycle", 230f, 680f);
            patrolCycle.AddChild(Node(new SetRandomNavMeshDestination
            {
                destination = patrolDestination,
                radius = patrolRadius
            }, 12, "Set Random Patrol Destination", 20f, 820f), 0);
            patrolCycle.AddChild(Node(new SetPatrolMovingAnimation(), 13, "Set Patrol Moving Animation", 145f, 820f), 1);
            patrolCycle.AddChild(Node(new MoveToNavMeshDestination
            {
                destination = patrolDestination,
                moveSpeed = walkSpeed
            }, 14, "Move To Patrol Destination", 270f, 820f), 2);
            patrolCycle.AddChild(Node(new SetPatrolIdleAnimation(), 15, "Set Patrol Idle Animation", 395f, 820f), 3);
            patrolCycle.AddChild(Node(new Wait
            {
                randomWait = true,
                randomWaitMin = idleTimeMin,
                randomWaitMax = idleTimeMax
            }, 16, "Random Idle Wait", 520f, 820f), 4);
            patrolRepeater.AddChild(patrolCycle, 0);
            parallel.AddChild(patrolRepeater, 0);

            var repeater = Node(new Repeater
            {
                repeatForever = true,
                endOnFailure = false
            }, 17, "Repeat Voice Mimic", 620f, 540f);
            var mimicCycle = Node(new Sequence(), 18, "VoiceMimicCycle", 620f, 680f);
            mimicCycle.AddChild(Node(new Wait
            {
                randomWait = true,
                randomWaitMin = mimicIntervalMin,
                randomWaitMax = mimicIntervalMax
            }, 19, "Random Mimic Wait", 700f, 820f), 0);
            mimicCycle.AddChild(Node(new PlayMimicVoice
            {
                mimicVoiceClips = mimicClips
            }, 20, "Play Mimic Voice", 850f, 820f), 1);
            repeater.AddChild(mimicCycle, 0);
            parallel.AddChild(repeater, 1);
            patrol.AddChild(parallel, 0);

            root.AddChild(attack, 0);
            root.AddChild(chase, 1);
            root.AddChild(patrol, 2);

            source.EntryTask = Node(new EntryTask(), 0, "Entry", 0f, 0f);
            source.RootTask = root;
            source.DetachedTasks = new List<Task>();
            BehaviorDesigner.Editor.BinarySerialization.Save(source);

            EditorUtility.SetDirty(behaviorTree);
            EditorUtility.SetDirty(monster);
            if (monster.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(monster.scene);

            return behaviorTree;
        }

        private static List<SharedVariable> CreateVariables(BehaviorSource source)
        {
            return new List<SharedVariable>
            {
                Variable(new SharedTransform { Value = GetValue<SharedTransform, Transform>(source, "PlayerTarget") }, "PlayerTarget"),
                Variable(new SharedFloat { Value = GetValue(source, "attackRange", 1.5f) }, "attackRange"),
                Variable(new SharedFloat { Value = GetValue(source, "mimicIntervalMin", 30f) }, "mimicIntervalMin"),
                Variable(new SharedFloat { Value = GetValue(source, "mimicIntervalMax", 90f) }, "mimicIntervalMax"),
                Variable(new SharedAudioClipList { Value = GetListValue<SharedAudioClipList, AudioClip>(source, "MimicVoiceClips") }, "MimicVoiceClips"),
                Variable(new SharedVector3(), "PatrolDestination"),
                Variable(new SharedFloat { Value = GetValue(source, "patrolRadius", 20f) }, "patrolRadius"),
                Variable(new SharedFloat { Value = GetValue(source, "walkSpeed", 2f) }, "walkSpeed"),
                Variable(new SharedFloat { Value = GetValue(source, "idleTimeMin", 2f) }, "idleTimeMin"),
                Variable(new SharedFloat { Value = GetValue(source, "idleTimeMax", 5f) }, "idleTimeMax"),
                Variable(new SharedVector3 { Value = GetValue<SharedVector3, Vector3>(source, "LastKnownPosition") }, "LastKnownPosition")
            };
        }

        private static float GetValue(BehaviorSource source, string name, float fallback)
        {
            SharedFloat variable = source.GetVariable(name) as SharedFloat;
            return variable != null ? variable.Value : fallback;
        }

        private static TValue GetValue<TVariable, TValue>(BehaviorSource source, string name)
            where TVariable : SharedVariable<TValue>
        {
            TVariable variable = source.GetVariable(name) as TVariable;
            return variable != null ? variable.Value : default;
        }

        private static List<TValue> GetListValue<TVariable, TValue>(BehaviorSource source, string name)
            where TVariable : SharedVariable<List<TValue>>
        {
            TVariable variable = source.GetVariable(name) as TVariable;
            return variable != null && variable.Value != null
                ? new List<TValue>(variable.Value)
                : new List<TValue>();
        }

        private static T Variable<T>(T variable, string name) where T : SharedVariable
        {
            variable.Name = name;
            variable.IsShared = true;
            return variable;
        }

        private static T Shared<T>(string name) where T : SharedVariable, new()
        {
            return Variable(new T(), name);
        }

        private static T Node<T>(T task, int id, string name, float x, float y) where T : Task
        {
            task.ID = id;
            task.FriendlyName = name;
            task.NodeData = new NodeData { Offset = new Vector2(x, y) };
            return task;
        }

        private static void SetAbortType(Composite composite, AbortType abortType)
        {
            FieldInfo field = typeof(Composite).GetField(
                "abortType",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field == null)
                throw new MissingFieldException(typeof(Composite).FullName, "abortType");

            field.SetValue(composite, abortType);
        }
    }

}
#endif

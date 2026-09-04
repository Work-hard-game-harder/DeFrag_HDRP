#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using BehaviorDesigner.Runtime;
using BehaviorDesigner.Runtime.Tasks;
using DeFrag.Monsters.B2F;
using DeFrag.Monsters.B2F.BehaviorDesignerTasks;
using DeFrag.Monsters.Common;
using DeFrag.Monsters.Common.BehaviorDesignerTasks;
using DeFrag.Combat;
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

            B2FMonsterVoiceMimic voiceMimic = monster.GetComponent<B2FMonsterVoiceMimic>();
            if (voiceMimic == null)
            {
                voiceMimic = recordUndo
                    ? Undo.AddComponent<B2FMonsterVoiceMimic>(monster)
                    : monster.AddComponent<B2FMonsterVoiceMimic>();
            }

            B2FPlayerVoicePerception voicePerception = monster.GetComponent<B2FPlayerVoicePerception>();
            if (voicePerception == null)
            {
                voicePerception = recordUndo
                    ? Undo.AddComponent<B2FPlayerVoicePerception>(monster)
                    : monster.AddComponent<B2FPlayerVoicePerception>();
            }

            B2FWorldNoisePerception worldNoisePerception = monster.GetComponent<B2FWorldNoisePerception>();
            if (worldNoisePerception == null)
            {
                worldNoisePerception = recordUndo
                    ? Undo.AddComponent<B2FWorldNoisePerception>(monster)
                    : monster.AddComponent<B2FWorldNoisePerception>();
            }

            B2FMonsterVision vision = monster.GetComponent<B2FMonsterVision>();
            if (vision == null)
            {
                vision = recordUndo
                    ? Undo.AddComponent<B2FMonsterVision>(monster)
                    : monster.AddComponent<B2FMonsterVision>();
            }

            MonsterAttackHitbox attackHitbox = monster.GetComponent<MonsterAttackHitbox>();
            if (attackHitbox == null)
            {
                attackHitbox = recordUndo
                    ? Undo.AddComponent<MonsterAttackHitbox>(monster)
                    : monster.AddComponent<MonsterAttackHitbox>();
                attackHitbox.ConfigureSphere(10, 1.5f);
            }

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 1f;

            BehaviorTree behaviorTree = monster.GetComponent<BehaviorTree>();
            if (behaviorTree == null)
                behaviorTree = recordUndo ? Undo.AddComponent<BehaviorTree>(monster) : monster.AddComponent<BehaviorTree>();

            NetworkMonsterPlayerTargetResolver targetResolver =
                monster.GetComponent<NetworkMonsterPlayerTargetResolver>();
            if (targetResolver == null)
            {
                targetResolver = recordUndo
                    ? Undo.AddComponent<NetworkMonsterPlayerTargetResolver>(monster)
                    : monster.AddComponent<NetworkMonsterPlayerTargetResolver>();
            }
            var serializedTree = new SerializedObject(behaviorTree);
            SerializedProperty restartWhenComplete = serializedTree.FindProperty("restartWhenComplete");
            if (restartWhenComplete != null)
            {
                restartWhenComplete.boolValue = true;
                serializedTree.ApplyModifiedPropertiesWithoutUndo();
            }

            BehaviorSource source = behaviorTree.GetBehaviorSource();
            source.SetAllVariables(CreateVariables(source));
            targetResolver.BindBehaviorTree(behaviorTree);

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
            SharedVector3 lastKnownPosition = Shared<SharedVector3>("LastKnownPosition");
            SharedFloat investigateDuration = Shared<SharedFloat>("investigateDuration");
            SharedFloat investigateRadius = Shared<SharedFloat>("investigateRadius");
            SharedFloat voicePositionUpdateInterval = Shared<SharedFloat>("voicePositionUpdateInterval");

            var root = Node(new Selector(), 1, "B2F Monster StateSelector", 0f, 110f);
            SetAbortType(root, AbortType.LowerPriority);

            var attack = Node(new Sequence(), 2, "Attack", -360f, 260f);
            SetAbortType(attack, AbortType.LowerPriority);
            attack.AddChild(Node(new WithinTargetDistance
            {
                target = playerTarget,
                distance = attackRange,
                ignoreHeight = new SharedBool { Value = true }
            }, 3, "Within Attack Range", -430f, 400f), 0);
            attack.AddChild(Node(new AttackPlayer
            {
                playerTarget = playerTarget
            }, 4, "Attack Player", -290f, 400f), 1);

            var chase = Node(new Sequence(), 5, "Chase", 0f, 260f);
            SetAbortType(chase, AbortType.Both);
            chase.AddChild(Node(new CanSeePlayer
            {
                playerTarget = playerTarget
            }, 6, "Can See Player", -70f, 400f), 0);
            chase.AddChild(Node(new ChaseTargetWithDetour
            {
                target = playerTarget,
                stoppingDistance = attackRange,
                ignoreHeight = new SharedBool { Value = true }
            }, 7, "Chase With Detour", 70f, 400f), 1);

            // IDs follow the serialized depth-first tree order. Behavior Designer 1.7.14 can
            // lose parent-child links when newly inserted branches use IDs after later siblings.
            var investigate = Node(new Sequence(), 8, "Investigate Sound", 300f, 260f);
            SetAbortType(investigate, AbortType.LowerPriority);
            investigate.AddChild(Node(new CanHearPlayerVoice
            {
                playerTarget = playerTarget,
                lastKnownPosition = lastKnownPosition
            }, 9, "Can Hear Voice Or World Noise", 230f, 400f), 0);
            investigate.AddChild(Node(new InvestigateHeardVoice
            {
                playerTarget = playerTarget,
                lastKnownPosition = lastKnownPosition,
                investigateDuration = investigateDuration,
                investigateRadius = investigateRadius,
                moveSpeed = walkSpeed,
                voicePositionUpdateInterval = voicePositionUpdateInterval
            }, 10, "Investigate Heard Sound", 370f, 400f), 1);

            var patrol = Node(new Sequence(), 11, "Patrol", 600f, 260f);
            var parallel = Node(new Parallel(), 12, "Patrol And Voice Mimic", 360f, 400f);

            var patrolRepeater = Node(new Repeater
            {
                repeatForever = true,
                endOnFailure = false
            }, 13, "Random Patrol Loop", 230f, 540f);
            var patrolCycle = Node(new Sequence(), 14, "Random Patrol Cycle", 230f, 680f);
            patrolCycle.AddChild(Node(new SetRandomNavMeshDestination
            {
                destination = patrolDestination,
                radius = patrolRadius
            }, 15, "Set Random Patrol Destination", 20f, 820f), 0);
            patrolCycle.AddChild(Node(new SetPatrolMovingAnimation(), 16, "Set Patrol Moving Animation", 145f, 820f), 1);
            patrolCycle.AddChild(Node(new MoveToNavMeshDestination
            {
                destination = patrolDestination,
                moveSpeed = walkSpeed
            }, 17, "Move To Patrol Destination", 270f, 820f), 2);
            patrolCycle.AddChild(Node(new SetPatrolIdleAnimation(), 18, "Set Patrol Idle Animation", 395f, 820f), 3);
            patrolCycle.AddChild(Node(new Wait
            {
                randomWait = true,
                randomWaitMin = idleTimeMin,
                randomWaitMax = idleTimeMax
            }, 19, "Random Idle Wait", 520f, 820f), 4);
            patrolRepeater.AddChild(patrolCycle, 0);
            parallel.AddChild(patrolRepeater, 0);

            var repeater = Node(new Repeater
            {
                repeatForever = true,
                endOnFailure = false
            }, 20, "Repeat Voice Mimic", 620f, 540f);
            var mimicCycle = Node(new Sequence(), 21, "VoiceMimicCycle", 620f, 680f);
            mimicCycle.AddChild(Node(new Wait
            {
                randomWait = true,
                randomWaitMin = mimicIntervalMin,
                randomWaitMax = mimicIntervalMax
            }, 22, "Random Mimic Wait", 700f, 820f), 0);
            mimicCycle.AddChild(Node(new PlayMimicVoice
            {
                mimicVoiceClips = mimicClips
            }, 23, "Play Mimic Voice", 850f, 820f), 1);
            repeater.AddChild(mimicCycle, 0);
            parallel.AddChild(repeater, 1);
            patrol.AddChild(parallel, 0);

            root.AddChild(attack, 0);
            root.AddChild(chase, 1);
            root.AddChild(investigate, 2);
            root.AddChild(patrol, 3);

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
                // 단독 실행에서는 기존 Inspector 대상을 유지하고, 네트워크 서버에서는
                // NetworkMonsterPlayerTargetResolver가 실제 PlayerObject로 교체합니다.
                Variable(new SharedTransform { Value = GetValue<SharedTransform, Transform>(source, "PlayerTarget") }, "PlayerTarget"),
                Variable(new SharedFloat { Value = GetValue(source, "attackRange", 1.5f) }, "attackRange"),
                Variable(new SharedFloat { Value = GetMimicInterval(source, "mimicIntervalMin", 2f) }, "mimicIntervalMin"),
                Variable(new SharedFloat { Value = GetMimicInterval(source, "mimicIntervalMax", 30f) }, "mimicIntervalMax"),
                Variable(new SharedAudioClipList { Value = GetListValue<SharedAudioClipList, AudioClip>(source, "MimicVoiceClips") }, "MimicVoiceClips"),
                Variable(new SharedVector3(), "PatrolDestination"),
                Variable(new SharedFloat { Value = GetValue(source, "patrolRadius", 20f) }, "patrolRadius"),
                Variable(new SharedFloat { Value = GetValue(source, "walkSpeed", 2f) }, "walkSpeed"),
                Variable(new SharedFloat { Value = GetValue(source, "idleTimeMin", 2f) }, "idleTimeMin"),
                Variable(new SharedFloat { Value = GetValue(source, "idleTimeMax", 5f) }, "idleTimeMax"),
                Variable(new SharedVector3 { Value = GetValue<SharedVector3, Vector3>(source, "LastKnownPosition") }, "LastKnownPosition"),
                Variable(new SharedFloat { Value = GetValue(source, "investigateDuration", 10f) }, "investigateDuration"),
                Variable(new SharedFloat { Value = GetValue(source, "investigateRadius", 5f) }, "investigateRadius"),
                Variable(new SharedFloat { Value = GetValue(source, "voicePositionUpdateInterval", 0.5f) }, "voicePositionUpdateInterval")
            };
        }

        private static float GetValue(BehaviorSource source, string name, float fallback)
        {
            SharedFloat variable = source.GetVariable(name) as SharedFloat;
            return variable != null ? variable.Value : fallback;
        }

        private static float GetMimicInterval(BehaviorSource source, string name, float fallback)
        {
            SharedFloat variable = source.GetVariable(name) as SharedFloat;
            if (variable == null)
                return fallback;

            // Migrates the previous 30-90 second defaults to the new 2-30 range.
            if (name == "mimicIntervalMin" && Mathf.Approximately(variable.Value, 30f))
                return 2f;
            if (name == "mimicIntervalMax" && Mathf.Approximately(variable.Value, 90f))
                return 30f;

            return Mathf.Clamp(variable.Value, 2f, 30f);
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

    /// <summary>
    /// Updates already loaded B2F monsters once after this feature is imported.
    /// The scene remains dirty so the developer can review and save the generated tree.
    /// </summary>
    [InitializeOnLoad]
    internal static class B2FVoiceDetectionTreeMigration
    {
        private const string SessionKey = "DeFrag.B2FVoiceDetectionTreeMigration.v3";

        static B2FVoiceDetectionTreeMigration()
        {
            EditorApplication.delayCall += UpgradeLoadedMonsters;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void UpgradeLoadedMonsters()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            B2FMonsterVoiceMimic[] monsters =
                UnityEngine.Object.FindObjectsByType<B2FMonsterVoiceMimic>(FindObjectsInactive.Include);
            if (monsters.Length == 0)
                return;

            SessionState.SetBool(SessionKey, true);

            foreach (B2FMonsterVoiceMimic mimic in monsters)
            {
                if (mimic == null || !mimic.gameObject.scene.IsValid())
                    continue;

                BehaviorTree tree = mimic.GetComponent<BehaviorTree>();
                if (tree != null && HasValidVoiceDetectionTree(tree.GetBehaviorSource().RootTask))
                    continue;

                B2FMonsterBehaviorTreeBuilder.Build(mimic.gameObject, true);
                Debug.Log($"[B2F Monster] '{mimic.name}'에 플레이어 음성 감지/조사 트리를 적용했습니다.", mimic);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += UpgradeLoadedMonsters;
        }

        private static bool HasValidVoiceDetectionTree(Task root)
        {
            return ContainsVoiceDetectionTask(root) &&
                   ContainsVisionTask(root) &&
                   HasNoEmptyParentTasks(root);
        }

        private static bool ContainsVoiceDetectionTask(Task task)
        {
            if (task == null)
                return false;
            if (task is CanHearPlayerVoice)
                return true;
            if (task is not ParentTask parent || parent.Children == null)
                return false;

            foreach (Task child in parent.Children)
            {
                if (ContainsVoiceDetectionTask(child))
                    return true;
            }

            return false;
        }

        private static bool ContainsVisionTask(Task task)
        {
            if (task == null)
                return false;
            if (task is CanSeePlayer)
                return true;
            if (task is not ParentTask parent || parent.Children == null)
                return false;

            foreach (Task child in parent.Children)
            {
                if (ContainsVisionTask(child))
                    return true;
            }

            return false;
        }

        private static bool HasNoEmptyParentTasks(Task task)
        {
            if (task == null)
                return false;
            if (task is not ParentTask parent)
                return true;
            if (parent.Children == null || parent.Children.Count == 0)
                return false;

            foreach (Task child in parent.Children)
            {
                if (!HasNoEmptyParentTasks(child))
                    return false;
            }

            return true;
        }
    }

}
#endif

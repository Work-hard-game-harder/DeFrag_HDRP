#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DeFrag.EditorTools
{
    /// <summary>
    /// 공용 네트워크 플레이어 Animator에 상체 전용 줍기/홀딩 레이어를 구성합니다.
    /// 같은 이름의 에셋과 상태를 재사용하므로 반복 실행해도 중복되지 않습니다.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerItemAnimatorInstaller
    {
        private const string ControllerPath =
            "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string PickupClipPath = "Assets/Animation/X Bot@Taking Item.fbx";
        private const string HoldingClipPath = "Assets/Animation/X Bot@Holding Item.fbx";
        private const string UpperBodyMaskPath = "Assets/Animation/PlayerUpperBody.mask";

        private const string LayerName = "Upper Body Item";
        private const string PickupParameter = "Pickup";
        private const string HoldingParameter = "IsHolding";
        private const string EmptyStateName = "Empty";
        private const string PickupStateName = "Picking Up";
        private const string HoldingStateName = "Holding Item";

        static PlayerItemAnimatorInstaller()
        {
            EditorApplication.delayCall += Install;
        }

        [MenuItem("Tools/DeFrag/Player/Install Item Pickup And Holding Animations")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (ConfigureClipImporter(PickupClipPath, PickupStateName, false) ||
                ConfigureClipImporter(HoldingClipPath, HoldingStateName, true))
            {
                EditorApplication.delayCall -= Install;
                EditorApplication.delayCall += Install;
                return;
            }

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimationClip pickupClip = LoadClip(PickupClipPath, PickupStateName);
            AnimationClip holdingClip = LoadClip(HoldingClipPath, HoldingStateName);

            if (controller == null || pickupClip == null || holdingClip == null)
            {
                Debug.LogWarning(
                    "[Player Item Animator] Controller 또는 Picking Up/Holding Item 클립을 찾지 못했습니다.");
                return;
            }

            AvatarMask upperBodyMask = GetOrCreateUpperBodyMask();
            EnsureParameter(controller, PickupParameter, AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, HoldingParameter, AnimatorControllerParameterType.Bool);

            AnimatorControllerLayer layer = GetOrCreateLayer(controller, upperBodyMask);
            AnimatorStateMachine stateMachine = layer.stateMachine;
            RemoveLegacyStatesFromOtherLayers(controller, stateMachine);

            AnimatorState emptyState = FindState(stateMachine, EmptyStateName) ??
                                       stateMachine.AddState(EmptyStateName, new Vector3(260f, 100f));
            AnimatorState pickupState = FindState(stateMachine, PickupStateName) ??
                                        stateMachine.AddState(PickupStateName, new Vector3(510f, 20f));
            AnimatorState holdingState = FindState(stateMachine, HoldingStateName) ??
                                         stateMachine.AddState(HoldingStateName, new Vector3(760f, 100f));

            emptyState.motion = null;
            pickupState.motion = pickupClip;
            holdingState.motion = holdingClip;
            emptyState.writeDefaultValues = false;
            pickupState.writeDefaultValues = false;
            holdingState.writeDefaultValues = false;
            stateMachine.defaultState = emptyState;

            EnsureAnyStatePickupTransition(stateMachine, pickupState);
            EnsureConditionalTransition(
                emptyState, holdingState, HoldingParameter, AnimatorConditionMode.If, false);
            EnsureConditionalTransition(
                pickupState, holdingState, HoldingParameter, AnimatorConditionMode.If, true);
            EnsureConditionalTransition(
                pickupState, emptyState, HoldingParameter, AnimatorConditionMode.IfNot, true);
            EnsureConditionalTransition(
                holdingState, emptyState, HoldingParameter, AnimatorConditionMode.IfNot, false);

            EditorUtility.SetDirty(upperBodyMask);
            EditorUtility.SetDirty(stateMachine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log(
                "[Player Item Animator] Upper Body Item 레이어와 Picking Up/Holding Item 상태를 구성했습니다.",
                controller);
        }

        private static bool ConfigureClipImporter(
            string path,
            string clipName,
            bool shouldLoop)
        {
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                return false;

            ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0
                ? importer.clipAnimations
                : importer.defaultClipAnimations;
            if (clips.Length == 0)
                return false;

            ModelImporterClipAnimation clip = clips[0];
            bool changed = importer.animationType != ModelImporterAnimationType.Human ||
                           importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
                           clip.name != clipName ||
                           clip.loopTime != shouldLoop ||
                           clip.loopPose != shouldLoop ||
                           !clip.lockRootRotation ||
                           !clip.lockRootHeightY ||
                           !clip.lockRootPositionXZ;
            if (!changed)
                return false;

            // 원본 Mixamo와 Player(H)의 뼈 길이가 다르므로 Copy From Other Avatar를
            // 사용하지 않습니다. 클립 자체 Avatar에서 Humanoid 리타게팅해야 안전합니다.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            clip.name = clipName;
            clip.loopTime = shouldLoop;
            clip.loopPose = shouldLoop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.lockRootPositionXZ = true;
            clips[0] = clip;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        private static AnimationClip LoadClip(string path, string preferredName)
        {
            AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(clip => !clip.name.StartsWith("__preview__"))
                .ToArray();

            return clips.FirstOrDefault(clip => clip.name == preferredName) ??
                   clips.FirstOrDefault();
        }

        private static AvatarMask GetOrCreateUpperBodyMask()
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask { name = "PlayerUpperBody" };
                AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            }

            for (AvatarMaskBodyPart part = AvatarMaskBodyPart.Root;
                 part < AvatarMaskBodyPart.LastBodyPart;
                 part++)
            {
                mask.SetHumanoidBodyPartActive(part, false);
            }

            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, true);
            mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, true);
            return mask;
        }

        private static void EnsureParameter(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            AnimatorControllerParameter parameter = controller.parameters
                .FirstOrDefault(candidate => candidate.name == parameterName);
            if (parameter == null)
            {
                controller.AddParameter(parameterName, parameterType);
                return;
            }

            if (parameter.type != parameterType)
            {
                Debug.LogError(
                    $"[Player Item Animator] '{parameterName}' 파라미터 타입이 {parameterType}이어야 합니다.",
                    controller);
            }
        }

        private static AnimatorControllerLayer GetOrCreateLayer(
            AnimatorController controller,
            AvatarMask mask)
        {
            AnimatorControllerLayer layer = controller.layers
                .FirstOrDefault(candidate => candidate.name == LayerName);
            if (layer == null)
            {
                AnimatorStateMachine stateMachine = new() { name = LayerName };
                AssetDatabase.AddObjectToAsset(stateMachine, controller);
                layer = new AnimatorControllerLayer
                {
                    name = LayerName,
                    stateMachine = stateMachine,
                    // 기본 자세에서는 Base Layer가 팔을 담당하고, 런타임에서
                    // 줍기/홀딩 중에만 이 레이어의 가중치를 1로 올립니다.
                    defaultWeight = 0f,
                    blendingMode = AnimatorLayerBlendingMode.Override,
                    avatarMask = mask
                };
                controller.AddLayer(layer);
                return controller.layers.First(candidate => candidate.name == LayerName);
            }

            layer.defaultWeight = 0f;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.avatarMask = mask;
            AnimatorControllerLayer[] layers = controller.layers;
            int index = System.Array.FindIndex(layers, candidate => candidate.name == LayerName);
            layers[index] = layer;
            controller.layers = layers;
            return controller.layers[index];
        }

        private static AnimatorState FindState(
            AnimatorStateMachine stateMachine,
            string stateName)
        {
            return stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state != null && state.name == stateName);
        }

        private static void RemoveLegacyStatesFromOtherLayers(
            AnimatorController controller,
            AnimatorStateMachine itemStateMachine)
        {
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                AnimatorStateMachine stateMachine = layer.stateMachine;
                if (stateMachine == null || stateMachine == itemStateMachine)
                    continue;

                foreach (ChildAnimatorState child in stateMachine.states.ToArray())
                {
                    if (child.state == null ||
                        (child.state.name != PickupStateName &&
                         child.state.name != HoldingStateName))
                    {
                        continue;
                    }

                    foreach (AnimatorStateTransition transition in
                             stateMachine.anyStateTransitions.ToArray())
                    {
                        if (transition.destinationState == child.state)
                            stateMachine.RemoveAnyStateTransition(transition);
                    }

                    stateMachine.RemoveState(child.state);
                }
            }
        }

        private static void EnsureAnyStatePickupTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState pickupState)
        {
            AnimatorStateTransition transition = stateMachine.anyStateTransitions
                .FirstOrDefault(candidate => candidate.destinationState == pickupState);
            if (transition == null)
                transition = stateMachine.AddAnyStateTransition(pickupState);

            RemoveDuplicateAnyStateTransitions(stateMachine, pickupState, transition);
            ReplaceConditions(
                transition,
                PickupParameter,
                AnimatorConditionMode.If);

            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.05f;
            transition.canTransitionToSelf = false;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        }

        private static void EnsureConditionalTransition(
            AnimatorState source,
            AnimatorState destination,
            string parameter,
            AnimatorConditionMode mode,
            bool hasExitTime)
        {
            AnimatorStateTransition transition = source.transitions
                .FirstOrDefault(candidate => candidate.destinationState == destination);
            if (transition == null)
                transition = source.AddTransition(destination);

            RemoveDuplicateTransitions(source, destination, transition);
            ReplaceConditions(transition, parameter, mode);

            transition.hasExitTime = hasExitTime;
            transition.exitTime = hasExitTime ? 0.95f : 0f;
            transition.hasFixedDuration = true;
            transition.duration = 0.1f;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        }

        private static void ReplaceConditions(
            AnimatorStateTransition transition,
            string parameter,
            AnimatorConditionMode mode)
        {
            foreach (AnimatorCondition condition in transition.conditions)
                transition.RemoveCondition(condition);

            transition.AddCondition(mode, 0f, parameter);
        }

        private static void RemoveDuplicateTransitions(
            AnimatorState source,
            AnimatorState destination,
            AnimatorStateTransition keep)
        {
            foreach (AnimatorStateTransition transition in source.transitions)
            {
                if (transition != keep && transition.destinationState == destination)
                    source.RemoveTransition(transition);
            }
        }

        private static void RemoveDuplicateAnyStateTransitions(
            AnimatorStateMachine stateMachine,
            AnimatorState destination,
            AnimatorStateTransition keep)
        {
            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                if (transition != keep && transition.destinationState == destination)
                    stateMachine.RemoveAnyStateTransition(transition);
            }
        }
    }
}
#endif

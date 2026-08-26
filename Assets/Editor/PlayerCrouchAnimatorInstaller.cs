using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace DeFrag.EditorTools
{
    /// <summary>
    /// 프로젝트의 공용 플레이어 Animator에 Crouching Idle 상태를 반복 가능하게 설치합니다.
    /// 동일한 이름의 상태와 전환을 재사용하므로 스크립트 재컴파일 시 중복 생성되지 않습니다.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerCrouchAnimatorInstaller
    {
        private const string ControllerPath =
            "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string ClipPath =
            "Assets/StarterAssets/ThirdPersonController/Character/Animations/X Bot@Crouching Idle.fbx";
        private const string CrouchParameter = "IsCrouching";
        private const string GroundedParameter = "Grounded";
        private const string CrouchStateName = "Crouching Idle";
        private const string LocomotionStateName = "Idle Walk Run Blend";
        private const string InAirStateName = "InAir";

        static PlayerCrouchAnimatorInstaller()
        {
            EditorApplication.delayCall += Install;
        }

        [MenuItem("Tools/DeFrag/Player/Install Crouch Animation")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            ConfigureClipLooping();

            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            AnimationClip crouchClip = AssetDatabase.LoadAllAssetsAtPath(ClipPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__"));

            if (controller == null || crouchClip == null)
            {
                Debug.LogWarning(
                    "[Player Crouch Animator] Controller 또는 Crouching Idle 클립을 찾지 못했습니다.");
                return;
            }

            if (!controller.parameters.Any(parameter => parameter.name == CrouchParameter))
                controller.AddParameter(CrouchParameter, AnimatorControllerParameterType.Bool);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState crouchState = FindState(stateMachine, CrouchStateName) ??
                                        stateMachine.AddState(CrouchStateName, new Vector3(540f, 390f));
            AnimatorState locomotionState = FindState(stateMachine, LocomotionStateName);
            AnimatorState inAirState = FindState(stateMachine, InAirStateName);

            crouchState.motion = crouchClip;
            crouchState.iKOnFeet = true;
            crouchState.writeDefaultValues = true;

            EnsureAnyStateToCrouch(stateMachine, crouchState);
            if (locomotionState != null)
                EnsureCrouchToLocomotion(crouchState, locomotionState);
            if (inAirState != null)
                EnsureCrouchToInAir(crouchState, inAirState);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[Player Crouch Animator] IsCrouching 상태와 전환을 설치했습니다.", controller);
        }

        private static void ConfigureClipLooping()
        {
            if (AssetImporter.GetAtPath(ClipPath) is not ModelImporter importer)
                return;

            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            bool changed = importer.clipAnimations.Length == 0;
            foreach (ModelImporterClipAnimation clip in clips)
            {
                if (!clip.loopTime || !clip.loopPose || !clip.lockRootHeightY ||
                    !clip.lockRootPositionXZ || !clip.lockRootRotation)
                {
                    clip.loopTime = true;
                    clip.loopPose = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = true;
                    clip.lockRootRotation = true;
                    changed = true;
                }
            }

            if (!changed)
                return;

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            return stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state != null && state.name == stateName);
        }

        private static void EnsureAnyStateToCrouch(
            AnimatorStateMachine stateMachine,
            AnimatorState crouchState)
        {
            if (stateMachine.anyStateTransitions.Any(transition =>
                    transition.destinationState == crouchState &&
                    HasCondition(transition, CrouchParameter, AnimatorConditionMode.If)))
                return;

            AnimatorStateTransition transition = stateMachine.AddAnyStateTransition(crouchState);
            ConfigureTransition(transition);
            transition.canTransitionToSelf = false;
            transition.AddCondition(AnimatorConditionMode.If, 0f, CrouchParameter);
            transition.AddCondition(AnimatorConditionMode.If, 0f, GroundedParameter);
        }

        private static void EnsureCrouchToLocomotion(
            AnimatorState crouchState,
            AnimatorState locomotionState)
        {
            if (crouchState.transitions.Any(transition =>
                    transition.destinationState == locomotionState &&
                    HasCondition(transition, CrouchParameter, AnimatorConditionMode.IfNot)))
                return;

            AnimatorStateTransition transition = crouchState.AddTransition(locomotionState);
            ConfigureTransition(transition);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, CrouchParameter);
            transition.AddCondition(AnimatorConditionMode.If, 0f, GroundedParameter);
        }

        private static void EnsureCrouchToInAir(AnimatorState crouchState, AnimatorState inAirState)
        {
            if (crouchState.transitions.Any(transition =>
                    transition.destinationState == inAirState &&
                    HasCondition(transition, GroundedParameter, AnimatorConditionMode.IfNot)))
                return;

            AnimatorStateTransition transition = crouchState.AddTransition(inAirState);
            ConfigureTransition(transition);
            transition.AddCondition(AnimatorConditionMode.IfNot, 0f, GroundedParameter);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = 0.1f;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
        }

        private static bool HasCondition(
            AnimatorStateTransition transition,
            string parameter,
            AnimatorConditionMode mode)
        {
            return transition.conditions.Any(condition =>
                condition.parameter == parameter && condition.mode == mode);
        }
    }
}

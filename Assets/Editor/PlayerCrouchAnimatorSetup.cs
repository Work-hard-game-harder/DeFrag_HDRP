#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 네트워크 PlayerCharacter가 공유하는 Animator Controller에 앉기 Idle 상태를
/// 누락 없이 구성합니다. 임포트/도메인 리로드 때 멱등적으로 검증됩니다.
/// </summary>
public static class PlayerCrouchAnimatorSetup
{
    private const string ControllerPath =
        "Assets/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
    private const string ClipPath =
        "Assets/StarterAssets/ThirdPersonController/Character/Animations/X Bot@Crouching Idle.fbx";
    private const string ParameterName = "IsCrouching";
    private const string CrouchStateName = "Crouching Idle";
    private const string LocomotionStateName = "Idle Walk Run Blend";

    [InitializeOnLoadMethod]
    private static void ScheduleAutomaticSetup()
    {
        EditorApplication.delayCall -= EnsureConfigured;
        EditorApplication.delayCall += EnsureConfigured;
    }

    [MenuItem("Tools/DeFrag/Player/Configure Crouch Animation")]
    public static void EnsureConfigured()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EnsureClipLoops())
        {
            EditorApplication.delayCall -= EnsureConfigured;
            EditorApplication.delayCall += EnsureConfigured;
            return;
        }

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

        if (!controller.parameters.Any(parameter => parameter.name == ParameterName))
            controller.AddParameter(ParameterName, AnimatorControllerParameterType.Bool);

        AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
        AnimatorState locomotionState = FindState(stateMachine, LocomotionStateName);
        if (locomotionState == null)
        {
            Debug.LogError(
                $"[Player Crouch Animator] '{LocomotionStateName}' 상태를 찾지 못했습니다.",
                controller);
            return;
        }

        AnimatorState crouchState = FindState(stateMachine, CrouchStateName);
        if (crouchState == null)
            crouchState = stateMachine.AddState(CrouchStateName, new Vector3(520f, 400f));

        crouchState.motion = crouchClip;
        crouchState.writeDefaultValues = true;

        AnimatorStateTransition enterTransition = stateMachine.anyStateTransitions
            .FirstOrDefault(transition => transition.destinationState == crouchState);
        if (enterTransition == null)
            enterTransition = stateMachine.AddAnyStateTransition(crouchState);

        ConfigureTransition(enterTransition, AnimatorConditionMode.If);
        enterTransition.canTransitionToSelf = false;

        AnimatorStateTransition exitTransition = crouchState.transitions
            .FirstOrDefault(transition => transition.destinationState == locomotionState);
        if (exitTransition == null)
            exitTransition = crouchState.AddTransition(locomotionState);

        ConfigureTransition(exitTransition, AnimatorConditionMode.IfNot);

        EditorUtility.SetDirty(crouchState);
        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[Player Crouch Animator] 네트워크 PlayerCharacter용 Crouching Idle 상태 구성이 완료되었습니다.",
            controller);
    }

    private static bool EnsureClipLoops()
    {
        ModelImporter importer = AssetImporter.GetAtPath(ClipPath) as ModelImporter;
        if (importer == null)
            return false;

        ModelImporterClipAnimation[] clips = importer.clipAnimations.Length > 0
            ? importer.clipAnimations
            : importer.defaultClipAnimations;
        bool changed = false;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            if (clip.loopTime && clip.loopPose)
                continue;

            clip.loopTime = true;
            clip.loopPose = true;
            changed = true;
        }

        if (!changed)
            return false;

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        return true;
    }

    private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
    {
        return stateMachine.states
            .Select(child => child.state)
            .FirstOrDefault(state => state != null && state.name == stateName);
    }

    private static void ConfigureTransition(
        AnimatorStateTransition transition,
        AnimatorConditionMode conditionMode)
    {
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = 0.1f;
        transition.offset = 0f;

        foreach (AnimatorCondition condition in transition.conditions)
            transition.RemoveCondition(condition);

        transition.AddCondition(conditionMode, 0f, ParameterName);
    }
}
#endif

using System.Collections;
using UnityEngine;

/// <summary>
/// 씬 진입 직후 기존 SubtitleTrigger를 한 번 재생하는 로컬 UI 프레젠테이션입니다.
/// 네트워크 게임 상태를 변경하지 않으며 각 클라이언트의 화면에서 독립적으로 동작합니다.
/// </summary>
[DisallowMultipleComponent]
public sealed class SubtitleSceneEntryPresentation : MonoBehaviour
{
    [SerializeField] private SubtitleTrigger subtitleTrigger;
    [SerializeField] private UISpriteSequencePlayer entryVisual;
    [Min(0f)] [SerializeField] private float startDelay;

    private Coroutine playRoutine;
    private bool hasPlayed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestartSceneEntryPresentations()
    {
        SubtitleSceneEntryPresentation[] presentations =
            FindObjectsByType<SubtitleSceneEntryPresentation>(FindObjectsInactive.Include);

        foreach (SubtitleSceneEntryPresentation presentation in presentations)
            presentation.BeginForCurrentPlaySession();
    }

    private void Awake()
    {
        AttachVisualToSubtitlePanel();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            BeginForCurrentPlaySession();
    }

    private void BeginForCurrentPlaySession()
    {
        if (!isActiveAndEnabled)
            return;

        if (playRoutine != null)
            StopCoroutine(playRoutine);

        hasPlayed = false;
        AttachVisualToSubtitlePanel();
        playRoutine = StartCoroutine(PlayAfterInitialization());
    }

    private IEnumerator PlayAfterInitialization()
    {
        // SubtitlesScript.Start가 초기 UI 상태를 정리한 다음 재생합니다.
        yield return null;

        // 씬 시작 시네마틱(LobbyF·B1F)이 재생 중이면 끝난 뒤에 시작합니다.
        while (LobbyIntroCinematic.IsPlaying)
            yield return null;

        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        playRoutine = null;
        PlayOnce();
    }

    public void PlayOnce()
    {
        if (hasPlayed)
            return;

        hasPlayed = true;
        AttachVisualToSubtitlePanel();

        if (subtitleTrigger == null)
        {
            Debug.LogWarning(
                $"[{nameof(SubtitleSceneEntryPresentation)}] Subtitle Trigger가 연결되지 않았습니다.",
                this);
            return;
        }

        subtitleTrigger.PlaySubtitleFromInteract();
    }

    private void AttachVisualToSubtitlePanel()
    {
        if (entryVisual == null || subtitleTrigger == null ||
            subtitleTrigger.subtitlesScript == null)
            return;

        GameObject subtitlePanel = subtitleTrigger.subtitlesScript.subtitlesPanel;
        if (subtitlePanel == null || entryVisual.transform.parent == subtitlePanel.transform)
            return;

        // 씬 루트에 저장된 비활성 Visual을 실제 자막 Canvas 아래로 옮깁니다.
        // worldPositionStays=false이므로 Inspector에 저장된 UI 좌표/크기를 그대로 사용합니다.
        entryVisual.transform.SetParent(subtitlePanel.transform, false);
    }

    private void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        hasPlayed = false;
        entryVisual?.StopAndHide();
    }
}

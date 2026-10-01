using EasyPeasyFirstPersonController;
using UnityEngine;

[System.Serializable]
public class SubtitleColorOverride
{
    [Min(0)] public int subtitleIndex;
    public Color color = Color.white;
}

[System.Serializable]
public sealed class SubtitleAudioOverride
{
    [Min(0)] public int subtitleIndex;
    public AudioClip audioClip;
}

public class SubtitleTrigger : MonoBehaviour
{
    public SubtitlesScript subtitlesScript; // 씬에 배치된 SubtitleBox 연결
    public string[] mySubtitles;            // 이 트리거에서 재생할 기존 자막 목록
    private bool hasTriggered = false;
    public GameObject walkietakie; // 워키토키 획득 시 활성화할 오브젝트

    [Header("Quest UI Link")]
    [Tooltip("체크하면 이 자막이 모두 끝난 뒤 공개 대기 중인 다음 퀘스트 UI를 표시합니다.")]
    [SerializeField] private bool revealPendingQuestAfterSubtitle;
    [SerializeField] private string requiredQuestId;
    [SerializeField] private string completionQuestSignal;

    [Header("Subtitle Color Overrides")]
    [SerializeField]
    private SubtitleColorOverride[] colorOverrides;
    [SerializeField]
    private SubtitleAudioOverride[] audioOverrides;

    [Header("Trigger Visual")]
    [Tooltip("이 Trigger의 자막 전체가 재생되는 동안 함께 표시할 이미지 시퀀스입니다.")]
    [SerializeField]
    private UISpriteSequencePlayer triggerVisual;

    [Header("Trigger Activation")]
    [Tooltip("플레이어가 Collider에 진입했을 때 자막을 자동으로 재생합니다.")]
    [SerializeField] private bool playOnPlayerEnter = true;

    public UISpriteSequencePlayer TriggerVisual => triggerVisual;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ResetSceneTriggerState()
    {
        // Reload Scene이 꺼진 Editor Play Mode에서도 이전 실행의 one-shot 상태가
        // 다음 실행으로 넘어가지 않게 합니다.
        SubtitleTrigger[] triggers =
            FindObjectsByType<SubtitleTrigger>(FindObjectsInactive.Include);
        foreach (SubtitleTrigger trigger in triggers)
            trigger.hasTriggered = false;
    }

    private void Awake()
    {
        if (triggerVisual != null)
            triggerVisual.StopAndHide();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!playOnPlayerEnter)
            return;

        TryPlayForPlayer(other);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!playOnPlayerEnter)
            return;

        TryPlayForPlayer(other);
    }

    private void TryPlayForPlayer(Collider other)
    {
        if (hasTriggered) return;
        // LobbyF 오프닝 시네마틱 중에는 대기했다가, 끝난 뒤 OnTriggerStay로 이어서 재생합니다.
        if (LobbyIntroCinematic.IsPlaying) return;
        var player = other.GetComponentInParent<StarterAssets.PersonController>();
        if (player != null && player.IsSpawned && !player.IsOwner) return;
        if (player != null || other.CompareTag("Player"))
        {
            if (!string.IsNullOrWhiteSpace(requiredQuestId) &&
                (QuestManager.Instance == null || !QuestManager.Instance.IsQuestActive(requiredQuestId))) return;
            // 퀘스트 공개용 트리거는 실제로 공개를 기다리는 퀘스트가 있을 때만
            // 실행되게 하여, 플레이어가 순서보다 먼저 진입해 트리거를 소모하지 않게 합니다.
            if (revealPendingQuestAfterSubtitle && string.IsNullOrWhiteSpace(completionQuestSignal) &&
                (QuestManager.Instance == null || !QuestManager.Instance.IsWaitingForSubtitleReveal))
            {
                return;
            }

            if (subtitlesScript == null || mySubtitles == null || mySubtitles.Length == 0)
            {
                Debug.LogWarning($"[{nameof(SubtitleTrigger)}] {name}에 재생할 자막이 설정되지 않았습니다.", this);
                return;
            }

            hasTriggered = true;
            PlaySubtitlesWithTriggerVisual(CompleteQuestLink);
        }
    }

    private void CompleteQuestLink()
    {
        if (!string.IsNullOrWhiteSpace(completionQuestSignal))
        {
            QuestManager.Instance?.ReportProgress(completionQuestSignal, gameObject.name);
        }

        if (revealPendingQuestAfterSubtitle)
            QuestManager.Instance?.RequestPendingQuestRevealAfterSubtitle();
    }

    // 무전기 등 UnityEvent(인스펙터)에서 연결하는 용도 - 매개변수 없음
    public void PlaySubtitleFromInteract()
    {
        PlaySubtitleFromInteract(null);
    }

    // 엘리베이터 등 코드에서 콜백을 넘기는 용도
    public void PlaySubtitleFromInteract(System.Action onComplete)
    {
        if (subtitlesScript == null || mySubtitles == null || mySubtitles.Length == 0)
        {
            Debug.LogWarning($"[{nameof(SubtitleTrigger)}] {name}에 재생할 자막이 설정되지 않았습니다.", this);
            onComplete?.Invoke();
            return;
        }

        // Trigger volumes remain one-shot, while an explicitly interacted hint
        // can be replayed without progressing its quest a second time.
        hasTriggered = true;

        System.Action combinedCallback = () =>
        {
            CompleteQuestLink();
            onComplete?.Invoke();
        };

        PlaySubtitlesWithTriggerVisual(combinedCallback);
    }

    private void PlaySubtitlesWithTriggerVisual(System.Action onComplete)
    {
        subtitlesScript.PlaySubtitles(
            mySubtitles,
            colorOverrides,
            audioOverrides,
            () =>
            {
                if (triggerVisual != null)
                    triggerVisual.StopAndHide();

                onComplete?.Invoke();
            },
            () =>
            {
                if (triggerVisual == null)
                    return;

                triggerVisual.gameObject.SetActive(true);
                triggerVisual.PlayFromBeginning();
            });
    }

    private void OnDisable()
    {
        if (triggerVisual != null)
            triggerVisual.StopAndHide();
    }

    /*
    // 이전 FirstPersonController 기반 워키토키 획득 방식입니다.
    // 현재 네트워크 플레이어에서는 사용하지 않습니다.
    private void OnMouseDown()
    {
        if (hasTriggered) return;
        if (CompareTag("Item"))
        {
            hasTriggered = true;
            FirstPersonController player = FindAnyObjectByType<FirstPersonController>();
            if (player != null) player.PickUpWakieTakie();
            gameObject.SetActive(false);
            subtitlesScript.PlaySubtitles(
                mySubtitles,
                colorOverrides,
                audioOverrides,
                CompleteQuestLink);
        }
    }
    */
}

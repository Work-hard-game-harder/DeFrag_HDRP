using EasyPeasyFirstPersonController;
using UnityEngine;

public class SubtitleTrigger : MonoBehaviour
{
    public SubtitlesScript subtitlesScript; // 씬에 배치된 SubtitleBox 연결
    public string[] mySubtitles;            // 이 트리거에서 재생할 기존 자막 목록
    private bool hasTriggered = false;
    public GameObject wakietakie; // 워키토키 획득 시 활성화할 오브젝트

    [Header("Quest UI Link")]
    [Tooltip("체크하면 이 자막이 모두 끝난 뒤 공개 대기 중인 다음 퀘스트 UI를 표시합니다.")]
    [SerializeField] private bool revealPendingQuestAfterSubtitle;

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;
        if (other.CompareTag("Player"))
        {
            // 퀘스트 공개용 트리거는 실제로 공개를 기다리는 퀘스트가 있을 때만
            // 실행되게 하여, 플레이어가 순서보다 먼저 진입해 트리거를 소모하지 않게 합니다.
            if (revealPendingQuestAfterSubtitle &&
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
            subtitlesScript.PlaySubtitles(
                mySubtitles,
                revealPendingQuestAfterSubtitle ? RevealPendingQuest : null);
        }
    }

    private static void RevealPendingQuest()
    {
        // 완료된 퀘스트가 다음 단계 공개를 기다리는 경우에만 UI를 표시합니다.
        QuestManager.Instance?.RevealPendingQuestAfterSubtitle();
    }

    /*
    private void OnMouseDown()
    {
        if (hasTriggered) return;
        if (CompareTag("Item"))
        {
            hasTriggered = true;
            FirstPersonController player = FindAnyObjectByType<FirstPersonController>();
            if (player != null) player.PickUpWakieTakie();
            gameObject.SetActive(false);
            subtitlesScript.PlaySubtitles(mySubtitles);
        }
    }

    */
}

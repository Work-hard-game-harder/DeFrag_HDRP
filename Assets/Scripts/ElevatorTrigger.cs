using UnityEngine;

public class ElevatorTrigger : MonoBehaviour
{
    private bool isTriggered = false;
    public SubtitleTrigger subtitleTrigger;

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered || !other.CompareTag("Player")) return;
        isTriggered = true;

        // 1. 먼저 퀘스트를 진행시켜 IsWaitingForSubtitleReveal을 true로 만든다
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(1);
        }

        // 2. 그 다음 자막 재생 → 끝나면 RevealPendingQuest가 정상적으로 동작
        if (subtitleTrigger != null)
        {
            subtitleTrigger.PlaySubtitleFromInteract(OnSequenceFinished);
        }
        else
        {
            OnSequenceFinished();
        }
    }

    private void OnSequenceFinished()
    {
        Destroy(gameObject);
    }
}
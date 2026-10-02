using StarterAssets;
using UnityEngine;

public class ElevatorTrigger : MonoBehaviour
{
    [SerializeField] private SubtitleTrigger subtitleTrigger;
    [Tooltip("이 퀘스트가 실제로 공개되어 활성화되면 트리거를 제거합니다.")]
    [SerializeField] private string removeWhenQuestId;

    private bool isTriggered;
    private QuestManager subscribedQuestManager;

    private void Start()
    {
        subscribedQuestManager = QuestManager.Instance;
        if (subscribedQuestManager == null)
        {
            Debug.LogError("[ElevatorTrigger] QuestManager가 연결되지 않았습니다.", this);
            return;
        }

        subscribedQuestManager.onQuestStepChanged += HandleQuestStepChanged;
        RemoveIfTargetQuestIsActive();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        PersonController player = other.GetComponentInParent<PersonController>();
        if (player != null)
        {
            // 자막은 각 클라이언트가 소유한 로컬 플레이어에게만 표시합니다.
            if (player.IsSpawned && !player.IsOwner)
                return;
        }
        else if (!other.CompareTag("Player"))
        {
            return;
        }

        isTriggered = true;

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
        // 마지막 퀘스트가 아직 공개되지 않았다면 오브젝트를 유지하여
        // 플레이어가 나갔다가 다시 들어왔을 때 안내 자막을 다시 볼 수 있습니다.
        if (!RemoveIfTargetQuestIsActive())
            isTriggered = false;
    }

    private void HandleQuestStepChanged()
    {
        RemoveIfTargetQuestIsActive();
    }

    private bool RemoveIfTargetQuestIsActive()
    {
        if (subscribedQuestManager == null ||
            string.IsNullOrWhiteSpace(removeWhenQuestId) ||
            !subscribedQuestManager.IsQuestActive(removeWhenQuestId))
        {
            return false;
        }

        subscribedQuestManager.onQuestStepChanged -= HandleQuestStepChanged;
        subscribedQuestManager = null;
        Destroy(gameObject);
        return true;
    }

    private void OnDestroy()
    {
        if (subscribedQuestManager != null)
            subscribedQuestManager.onQuestStepChanged -= HandleQuestStepChanged;
    }
}

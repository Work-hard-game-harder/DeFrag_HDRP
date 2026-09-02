using UnityEngine;
using Unity.Netcode;

public class QuestTriggerZone : MonoBehaviour
{
    [Header("이 트리거가 작동해야 하는 퀘스트 인덱스")]
    public int targetStepIndex;
    [Tooltip("권장: 순서가 바뀌어도 안전한 Quest ID. 비어 있을 때만 인덱스를 사용합니다.")]
    [SerializeField] private string targetQuestId;

    [Header("Quest Signal")]
    [SerializeField] private string questSignal;
    [SerializeField] private string questSourceId;

    [Header("충돌 체크할 플레이어 태그")]
    public string playerTag = "Player";

    private void OnTriggerEnter(Collider other)
    {
        if (QuestManager.Instance == null) return;

        // 1. 부딪힌 오브젝트가 플레이어인지 확인
        if (other.CompareTag(playerTag))
        {
            NetworkObject playerObject = other.GetComponentInParent<NetworkObject>();
            if (playerObject != null && playerObject.IsSpawned && !playerObject.IsOwner)
                return;

            bool correctQuest = !string.IsNullOrWhiteSpace(targetQuestId)
                ? QuestManager.Instance.IsQuestActive(targetQuestId)
                : QuestManager.Instance.GetCurrentStepIndex() == targetStepIndex;

            if (correctQuest)
            {
                Debug.Log($"트리거 구역 통과! {targetStepIndex}번 퀘스트 완료 신호를 보냅니다.");
                bool reported = false;
                if (!string.IsNullOrWhiteSpace(questSignal))
                {
                    string source = string.IsNullOrWhiteSpace(questSourceId)
                        ? gameObject.name
                        : questSourceId;
                    reported = QuestManager.Instance.ReportProgress(questSignal, source);
                }

                if (reported)
                    gameObject.SetActive(false);
            }
        }
    }
}

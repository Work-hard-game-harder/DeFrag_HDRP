using UnityEngine;

public class QuestTriggerZone : MonoBehaviour
{
    [Header("이 트리거가 작동해야 하는 퀘스트 인덱스")]
    public int targetStepIndex;

    [Header("충돌 체크할 플레이어 태그")]
    public string playerTag = "Player";

    private void OnTriggerEnter(Collider other)
    {
        if (QuestManager.Instance == null) return;

        // 1. 부딪힌 오브젝트가 플레이어인지 확인
        if (other.CompareTag(playerTag))
        {
            int currentIndex = QuestManager.Instance.GetCurrentStepIndex();

            // 2. 현재 퀘스트 단계가 이 트리거가 작동해야 하는 단계가 맞는지 확인
            if (currentIndex == targetStepIndex)
            {
                Debug.Log($"트리거 구역 통과! {targetStepIndex}번 퀘스트 완료 신호를 보냅니다.");
                
                // 퀘스트 카운트 올리기 (TargetCount가 1이라면 바로 다음 퀘스트로 넘어감)
                QuestManager.Instance.ProgressActiveQuest(1);

                // 한 번 사용된 트리거는 중복 실행 방지를 위해 비활성화
                gameObject.SetActive(false);
            }
        }
    }
}
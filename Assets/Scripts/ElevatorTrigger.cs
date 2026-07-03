using UnityEngine;

public class ElevatorTrigger : MonoBehaviour
{
    private bool isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered || !other.CompareTag("Player")) return;
        isTriggered = true;

        // 상시 켜져 있는 QuestManager에게 "이제 1번 퀘스트(단서 모으기)로 전환해!" 라고 명령
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.SetQuestStep(1); 
        }

        // 역할을 다했으므로 트리거 콜라이더 무력화 및 삭제
        Destroy(gameObject);
    }
}
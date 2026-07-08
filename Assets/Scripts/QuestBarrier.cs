using UnityEngine;

public class QuestBarrier : MonoBehaviour
{
    [Header("이 벽이 플레이어를 막아야 하는 퀘스트 인덱스")]
    public int blockingStepIndex;

    private Collider barrierCollider;
    private bool isSubscribed = false;

    void Awake()
    {
        barrierCollider = GetComponent<Collider>();
    }

    void Start()
    {
        // Start 시점에 안전하게 이벤트를 구독합니다.
        TrySubscribe();
        UpdateBarrierState();
    }

    void TrySubscribe()
    {
        if (isSubscribed) return;
        
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.onQuestStepChanged += UpdateBarrierState;
            isSubscribed = true;
        }
    }

    void OnDestroy()
    {
        if (isSubscribed && QuestManager.Instance != null)
        {
            QuestManager.Instance.onQuestStepChanged -= UpdateBarrierState;
        }
    }

    void UpdateBarrierState()
    {
        if (QuestManager.Instance == null) return;

        int currentIndex = QuestManager.Instance.GetCurrentStepIndex();
        Debug.Log($"[벽 체크] 현재 퀘스트 인덱스: {currentIndex} / 내가 막는 인덱스: {blockingStepIndex}");

        // 현재 진행 중인 퀘스트가 내가 막아야 하는 단계일 때만 콜라이더를 켭니다.
        if (currentIndex == blockingStepIndex)
        {
            if (barrierCollider != null) barrierCollider.enabled = true; 
            Debug.Log("조건 일치: 투명벽 활성화 (못 지나감)");
        }
        else
        {
            if (barrierCollider != null) barrierCollider.enabled = false; 
            Debug.Log("조건 불일치: 투명벽 비활성화 (지나갈 수 있음)");
        }
    }
}
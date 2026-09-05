using UnityEngine;

[RequireComponent(typeof(Collider))]
public class QuestBarrier : MonoBehaviour
{
    [Header("이 Barrier가 활성화될 퀘스트")]
    [Tooltip("권장: 순서가 바뀌어도 안전한 Quest ID. 비어 있을 때만 기존 인덱스를 사용합니다.")]
    [SerializeField] private string blockingQuestId;

    [Tooltip("기존 씬 호환용 인덱스입니다. Blocking Quest ID가 비어 있을 때만 사용합니다.")]
    public int blockingStepIndex;

    private Collider barrierCollider;
    private QuestManager subscribedManager;
    private bool storyDebugBypassed;

    private void Awake()
    {
        barrierCollider = GetComponent<Collider>();
    }

    private void Start()
    {
        TrySubscribe();
        UpdateBarrierState();
    }

    private void TrySubscribe()
    {
        if (subscribedManager != null || QuestManager.Instance == null)
            return;

        subscribedManager = QuestManager.Instance;
        subscribedManager.onQuestStepChanged += UpdateBarrierState;
    }

    private void OnDestroy()
    {
        if (subscribedManager != null)
            subscribedManager.onQuestStepChanged -= UpdateBarrierState;
    }

    public void SetStoryDebugBypassed(bool bypassed)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        storyDebugBypassed = bypassed;
        if (barrierCollider == null)
            barrierCollider = GetComponent<Collider>();

        if (storyDebugBypassed && barrierCollider != null)
            barrierCollider.enabled = false;
        else
            UpdateBarrierState();
#endif
    }

    private void UpdateBarrierState()
    {
        if (barrierCollider == null)
            return;
        if (storyDebugBypassed)
        {
            barrierCollider.enabled = false;
            return;
        }
        if (QuestManager.Instance == null)
            return;

        // 단순 인덱스 비교가 아니라 실제 활성 상태를 사용합니다.
        // 퀘스트 완료 후 다음 자막을 기다리는 동안에는 이전 Barrier가 즉시 해제됩니다.
        barrierCollider.enabled = !string.IsNullOrWhiteSpace(blockingQuestId)
            ? QuestManager.Instance.IsQuestActive(blockingQuestId)
            : QuestManager.Instance.IsQuestActive(blockingStepIndex);
    }
}

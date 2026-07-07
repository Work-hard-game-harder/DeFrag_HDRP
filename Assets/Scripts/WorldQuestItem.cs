using UnityEngine;
using UnityEngine.Events;

public class WorldQuestItem : MonoBehaviour, IInteractable
{
    [Header("월드 상호작용 설정")]
    public string itemName = "무전기";
    public bool isHoldInteraction = false; // 기본적으로 주울 때는 딸깍(false) 추천

    [Header("트리거 성공 시 실행할 추가 이벤트")]
    // 여기에 팀원의 무전기 줍기 함수(PickUpWakieTakie) 등을 연결하세요.
    public UnityEvent onInteractEvent; 

    public string GetInteractionText()
    {
        return isHoldInteraction ? $"{itemName} 획득 (E 꾹 누르기)" : $"{itemName} 획득 (E)";
    }

    public bool IsHoldInteraction() => isHoldInteraction;

    public void Interact(PlayerInteraction player)
    {
        Debug.Log($"[월드 아이템] '{itemName}' 작동/획득 완료.");

        // 1. 퀘스트 매니저 카운트 증가
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.ProgressActiveQuest(1);
        }

        // 2. 인스펙터에 연결된 팀원의 고유 기능 실행
        if (onInteractEvent != null)
        {
            onInteractEvent.Invoke();
        }

        // 3. UI 닫아주고 오브젝트 파괴
        player.CloseAllUI();
        Destroy(gameObject);
    }
}
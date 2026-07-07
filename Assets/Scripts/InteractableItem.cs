using UnityEngine;
using UnityEngine.Events;

public class InteractableItem : MonoBehaviour, IInteractable
{
    public string itemName = "의문의 문서";
    public Sprite hintSprite;             // 힌트 이미지 (단서가 아니라면 비워둠)
    public bool isHoldInteraction = true;    // 꾹 누를지(true), 딸깍 누를지(false) 선택
    
    [HideInInspector]
    public bool isInteracted = false;     // 최초 상호작용 여부 체크

    [Header("상호작용 성공 시 실행할 팀원 기능 연동")]
    // 자막을 제외한 무전기 습득(PickUpWakieTakie) 등 고유 기능을 연결하는 곳입니다.
    public UnityEvent onInteractEvent; 

    // 1. HUD 텍스트 반환
    public string GetInteractionText()
    {
        if (isInteracted)
        {
            return $"{itemName} 다시 보기 (E)";
        }
        else
        {
            string suffix = isHoldInteraction ? " (E 꾹 누르기)" : " (E)";
            return $"{itemName} {suffix}";
        }
    }

    // 2. 상호작용 방식 결정 (이미 읽었다면 딸깍으로 전환)
    public bool IsHoldInteraction()
    {
        if (isInteracted) return false;
        return isHoldInteraction;
    }

    // 3. 상호작용 실행 (자막 기능 X)
    public void Interact(PlayerInteraction player)
    {
        // [최초 상호작용 일 때]
        if (!isInteracted)
        {
            isInteracted = true;
            Debug.Log($"[상호작용] '{itemName}' 처리 완료.");

            // 퀘스트 매니저 카운트 증가
            if (QuestManager.Instance != null)
            {
                QuestManager.Instance.ProgressActiveQuest(1);
            }

            // ★ 인스펙터에서 연결해 둔 팀원의 기능(예: 무전기 줍기 함수)을 실행합니다.
            if (onInteractEvent != null)
            {
                onInteractEvent.Invoke();
            }
        }
        else
        {
            Debug.Log($"'{itemName}' 재확인.");
        }

        // 단서용 힌트 이미지가 있는 경우에만 힌트 UI 작동
        if (hintSprite != null && player != null)
        {
            player.hintImage.sprite = hintSprite;
            player.hintPanel.SetActive(true);
            player.TogglePlayerControl(false); // 조작 차단
        }
        else
        {
            // 무전기처럼 화면에 단서 이미지를 띄울 필요가 없는 오브젝트라면
            // 조작을 차단하지 않고 HUD만 깔끔하게 지워줍니다.
            player.CloseAllUI();
        }
    }
}
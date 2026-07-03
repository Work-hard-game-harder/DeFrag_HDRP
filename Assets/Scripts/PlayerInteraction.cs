using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Raycast 설정")]
    public float interactionDistance = 3f;

    [Header("UI 설정")]
    public GameObject interactionHUD;
    public TextMeshProUGUI itemText;
    public Image progressCircle;

    [Header("Hint UI")]
    public GameObject hintPanel;
    public Image hintImage;

    [Header("조작 차단 대상 스크립트")]
    // 본인 프로젝트의 퍼스트 퍼슨 무브먼트/마우스 룩 스크립트를 인스펙터에서 연결하세요.
    public MonoBehaviour playerMovementScript; 
    public MonoBehaviour mouseLookScript;

    private float holdTime = 1.5f;
    private float currentHoldTime = 0f;
    private bool isUIOpen = false;
    
    private InteractableItem targetItem;
    private ElevatorPanel targetElevator; // ★ 엘레베이터 상호작용을 위해 추가

    private int myClueCount = 0; // 플레이어 개인 단서 카운트


    void Update()
    {
        // UI(힌트창 또는 엘레베이터 패널)가 열려있을 때 처리
        if (isUIOpen)
        {
            // ESC나 마우스 우클릭을 누르면 열려있는 모든 UI를 닫고 조작을 복구합니다.
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CloseAllUI();
            }
            return; // UI가 켜져있을 땐 아래의 상호작용 조준/입력 로직을 전부 건너뜁니다.
        }
        
        CheckInteractable();

        // 조준된 대상(단서 아이템 혹은 엘레베이터)이 있을 때만 입력을 처리합니다.
        if (targetItem != null || targetElevator != null)
        {
            HandleInteractionInput();
        }
    }

    void CheckInteractable()
    {
        // ★ 힌트 상호작용은 '언제든지(퀘스트 비활성화 상태여도)' 가능해야 하므로 QuestManager 체크 조건을 하단으로 이동시켰습니다.

        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        float sphereRadius = 0.3f;   // 판정 두께 설정
        float maxDistance = 7f;     // 사거리 설정

        if (Physics.SphereCast(ray, sphereRadius, out hit, maxDistance))
        {
            // 1. 단서 아이템 조준 검사
            InteractableItem item = hit.collider.GetComponent<InteractableItem>();
            if (item != null)
            {
                // 이미 한 번 읽은 단서라면 퀘스트 상태와 무관하게 언제든 다시 볼 수 있도록 허용
                // 아직 안 읽은 단서라면 퀘스트가 활성화(isClueQuestActive)되어 있을 때만 타겟으로 인정
                if (item.isInteracted || (QuestManager.Instance != null && QuestManager.Instance.isClueQuestActive))
                {
                    targetItem = item;
                    targetElevator = null; // 엘레베이터 타겟 해제

                    if (interactionHUD != null && !interactionHUD.activeSelf)
                    {
                        interactionHUD.SetActive(true);
                    }
                    return;
                }
            }

            // 2. 엘레베이터 패널 조준 검사 (처음부터 언제든지 조준 가능)
            ElevatorPanel elevator = hit.collider.GetComponent<ElevatorPanel>();
            if (elevator != null)
            {
                targetElevator = elevator;
                targetItem = null; // 아이템 타겟 해제

                if (interactionHUD != null && !interactionHUD.activeSelf)
                {
                    interactionHUD.SetActive(true);
                }
                return;
            }
        }

        // 아무것도 조준하지 않고 있다면 타겟을 초기화합니다.
        ResetTarget();
    }

    void HandleInteractionInput()
    {
        // [케이스 A] 엘레베이터를 조준했거나, 이미 한 번 읽었던 단서를 조준한 경우 -> 꾹 누르기 없이 '딸깍(단타)'으로 즉시 상호작용
        if (targetElevator != null || (targetItem != null && targetItem.isInteracted))
        {
            // 꾹 누르기 게이지 UI는 보이지 않도록 숨김 및 초기화
            if (progressCircle != null) progressCircle.fillAmount = 0f;

            if (Input.GetKeyDown(KeyCode.E))
            {
                ExecuteImmediateInteraction();
            }
            return;
        }

        // [케이스 B] 처음 발견한 단서 아이템인 경우 -> 기존 기획대로 1.5초 동안 '꾹 누르기' 작동
        if (Input.GetKey(KeyCode.E))
        {
            currentHoldTime += Time.deltaTime;
            if (progressCircle != null) progressCircle.fillAmount = currentHoldTime / holdTime;
            
            if (currentHoldTime >= holdTime)
            {
                ExecuteHoldInteraction();
            }
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            ResetTimer();
        }
    }
    
    // 처음 단서를 발견하여 꾹 눌러 게이지를 채웠을 때 실행되는 함수
    void ExecuteHoldInteraction()
    {
        if (targetItem == null) return;

        // 힌트 이미지 띄우기
        if (targetItem.hintSprite != null)
        {
            hintImage.sprite = targetItem.hintSprite;
            hintPanel.SetActive(true);
        }

        // 첫 상호작용일 때만 개인 카운트 및 퀘스트 매니저 카운트를 올려줍니다.
        myClueCount++;
        Debug.Log($"[최초] 단서 획득! 현재 단서 수: {myClueCount} / 6");

        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.AddClue(); 
        }

        targetItem.isInteracted = true; // ★ 이제 이 아이템은 다음부터 단타(딸깍)로 작동하게 설정됨

        // 상호작용이 끝났으므로 타겟을 풀고 HUD 및 타이머 초기화
        if (interactionHUD != null) interactionHUD.SetActive(false);
        ResetTimer();

        // ★ 화면을 보고 있는 동안 움직임 및 시점을 차단하고 UI 상태 활성화
        TogglePlayerControl(false); 
    }

    // 이미 읽은 단서나 엘레베이터를 '딸깍' 눌렀을 때 즉시 실행되는 함수
    void ExecuteImmediateInteraction()
    {
        if (targetItem != null) // 단서 재확인
        {
            if (targetItem.hintSprite != null)
            {
                hintImage.sprite = targetItem.hintSprite;
                hintPanel.SetActive(true);
            }
            Debug.Log("단서 다시 보기 (카운트가 올라가지 않습니다)");
        }
        else if (targetElevator != null) // 엘레베이터 도어락 키패드 열기
        {
            targetElevator.OpenKeypad();
        }

        if (interactionHUD != null) interactionHUD.SetActive(false);
        ResetTimer();

        // ★ 화면을 보고 있는 동안 움직임 및 시점을 차단하고 UI 상태 활성화
        TogglePlayerControl(false);
    }

    void CloseAllUI()
    {
        if (hintPanel != null) hintPanel.SetActive(false);
        if (targetElevator != null) targetElevator.CloseKeypad();

        // UI를 모두 닫았으므로 플레이어 조작을 정상으로 돌려놓습니다.
        TogglePlayerControl(true);
        ResetTarget();
    }

    void ResetTimer()
    {
        currentHoldTime = 0f;
        if (progressCircle != null) progressCircle.fillAmount = 0f;
    }

    void ResetTarget()
    {
        targetItem = null;
        targetElevator = null;
        if (interactionHUD != null && interactionHUD.activeSelf) 
            interactionHUD.SetActive(false);
    }

    // 플레이어 움직임 및 시점 ON/OFF 제어 함수
    void TogglePlayerControl(bool enable)
    {
        if (playerMovementScript != null) playerMovementScript.enabled = enable;
        if (mouseLookScript != null) mouseLookScript.enabled = enable;

        // 마우스 커서 락 상태 제어 (창이 열리면 커서를 풀고 움직이게 해줍니다)
        Cursor.lockState = enable ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !enable;
        
        isUIOpen = !enable;
    }
}
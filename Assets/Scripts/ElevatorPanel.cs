using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class ElevatorPanel : MonoBehaviour, IInteractable
{
    [Header("도어락 UI 연결")]
    [SerializeField] private GameObject keypadUIPanel;
    [SerializeField] private TextMeshProUGUI passwordText;
    [SerializeField] private TextMeshProUGUI errorText;

    [Header("비밀번호 및 탈출 설정")]
    [SerializeField] private string correctPassword = "361025";
    [SerializeField] private string nextSceneName = "B2_Floor";

    // ★ [하드코딩 방지] 퀘스트 순서가 바뀌면 인스펙터에서 이 숫자만 딸깍 고치면 됩니다.
    [Header("상호작용 조건 (QuestManager 연동)")]
    [Tooltip("엘리베이터를 열기 위해 현재 활성화되어야 하는 퀘스트 인덱스 번호")]
    [SerializeField] private int requiredQuestIndex = 4; 

    [Header("상호작용 HUD 문구")]
    [SerializeField] private string activeInteractionText = "키패드 열기 (E)";
    [SerializeField] private string lockedInteractionText = "아직 엘리베이터에 \n 접근할 수 없다.";

    private string currentInput = "";
    private bool isKeypadActive = false;
    private PlayerInteraction savedPlayer;

    void Start()
    {
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!isKeypadActive) return;

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CloseKeypad();
            return;
        }

        // 숫자 입력 감지
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
            {
                AppendCharacter(i.ToString());
                return;
            }
        }

        // 알파벳 입력 감지
        for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++)
        {
            if (Input.GetKeyDown(key))
            {
                AppendCharacter(key.ToString());
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Backspace) && currentInput.Length > 0)
        {
            currentInput = currentInput.Substring(0, currentInput.Length - 1);
            UpdateDisplay();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CheckPassword();
        }
    }

    // ===== IInteractable 구현 =====

    public bool IsHoldInteraction() => false;

    // 현재 진행 중인 퀘스트 인덱스를 가져와 조건부로 HUD 가이드 텍스트 변경
    public string GetInteractionText()
    {
        return IsConditionMet() ? activeInteractionText : lockedInteractionText;
    }

    public void Interact(PlayerInteraction player)
    {
        if (!IsConditionMet())
        {
            Debug.Log($"[ElevatorPanel] 필수 조건(퀘스트 인덱스 {requiredQuestIndex})을 만족하지 않아 상호작용이 거부되었습니다.");
            return; 
        }

        savedPlayer = player;
        OpenKeypad();
        player.TogglePlayerControl(false);
    }

    // ===== 객체지향 조건 검증 구역 =====

    private bool IsConditionMet()
    {
        if (QuestManager.Instance == null)
        {
            Debug.LogWarning("[ElevatorPanel] 월드에 QuestManager가 존재하지 않습니다.");
            return false;
        }

        // QuestManager에 새로 개설한 IsQuestActive 함수를 통해 현재 4번 퀘스트가 진행 중인지 검증
        return QuestManager.Instance.IsQuestActive(requiredQuestIndex);
    }

    // ===== 내부 UI 제어 로직 =====

    public void OpenKeypad()
    {
        isKeypadActive = true;
        currentInput = "";
        UpdateDisplay();
        if (keypadUIPanel != null) keypadUIPanel.SetActive(true);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    public void CloseKeypad()
    {
        isKeypadActive = false;
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        
        if (savedPlayer != null)
        {
            savedPlayer.TogglePlayerControl(true);
            savedPlayer = null;
        }
    }

    void AppendCharacter(string ch)
    {
        if (currentInput.Length >= 6) return;
        currentInput += ch;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (passwordText != null)
        {
            passwordText.text = currentInput;
            int remaining = 6 - currentInput.Length;
            for (int i = 0; i < remaining; i++)
            {
                passwordText.text += " _";
            }
        }
    }

    void CheckPassword()
    {
        if (currentInput == correctPassword)
        {
            Debug.Log("암호 일치! 다음 층으로 이동합니다.");
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            Debug.Log("암호 불일치!");
            if (errorText != null)
            {
                StopAllCoroutines();
                StartCoroutine(ShowErrorRoutine());
            }
            currentInput = "";
            UpdateDisplay();
        }
    }

    IEnumerator ShowErrorRoutine()
    {
        errorText.text = "틀린 암호입니다.";
        errorText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        errorText.gameObject.SetActive(false);
    }
}
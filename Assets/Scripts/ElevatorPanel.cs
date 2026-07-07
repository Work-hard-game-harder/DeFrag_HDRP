using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class ElevatorPanel : MonoBehaviour, IInteractable
{
    [Header("도어락 UI 연결")]
    public GameObject keypadUIPanel;      // 도어락 UI 전체 판넬
    public TextMeshProUGUI passwordText;  // 번호가 표시될 TMP 텍스트
    public TextMeshProUGUI errorText;     // "틀린 암호입니다" 경고 텍스트

    [Header("비밀번호 설정")]
    public string correctPassword = "361025"; // 정답 암호 6자리, 기획에 맞게 수정
    public string nextSceneName = "B2_Floor"; // 다음 층 씬 이름

    [Header("상호작용 설정")]
    public string interactionText = "키패드 열기(E키로 열기)"; // HUD에 표시될 문구

    private string currentInput = "";
    private bool isKeypadActive = false;

    void Start()
    {
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!isKeypadActive) return;

        // ESC나 우클릭으로 키패드 닫기
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            CloseKeypad();
            return;
        }

        // 키보드 넘패드 및 상단 숫자 패드 입력 감지
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(i.ToString()) || Input.GetKeyDown("[" + i + "]"))
            {
                AppendNumber(i.ToString());
            }
        }

        // 백스페이스 키로 지우기
        if (Input.GetKeyDown(KeyCode.Backspace) && currentInput.Length > 0)
        {
            currentInput = currentInput.Substring(0, currentInput.Length - 1);
            UpdateDisplay();
        }

        // 엔터 키로 정답 제출 (일반 엔터 & 키패드 엔터)
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            CheckPassword();
        }
    }

    // ===== IInteractable 구현 =====

    public bool IsHoldInteraction()
    {
        return false; // 꾹 누르기 없이 딸깍(단타)으로 키패드 오픈
    }

    public string GetInteractionText()
    {
        return interactionText;
    }

    public void Interact(PlayerInteraction player)
    {
        OpenKeypad();
        player.TogglePlayerControl(false); // 키패드 여는 동안 플레이어 조작 차단
    }

    // ===== 기존 로직 =====

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
    }

    void AppendNumber(string num)
    {
        if (currentInput.Length >= 6) return; // 6자리 제한
        currentInput += num;
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (passwordText != null)
        {
            passwordText.text = currentInput;

            // 빈자리 언더바 표시 연출 (예: 36_ _ _ _)
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
            SceneManager.LoadScene(nextSceneName); // 씬 전환
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

    // 틀렸을 때 경고문구가 2초 동안 반짝였다가 사라지는 효과
    IEnumerator ShowErrorRoutine()
    {
        errorText.text = "틀린 암호입니다.";
        errorText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        errorText.gameObject.SetActive(false);
    }
}
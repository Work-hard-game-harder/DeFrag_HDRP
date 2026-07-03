using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;

public class ElevatorPanel : MonoBehaviour
{
    [Header("도어락 UI 연결")]
    public GameObject keypadUIPanel;      // 도어락 UI 전체 판넬
    public TextMeshProUGUI passwordText;  // 번호가 표시될 TMP 텍스트
    public TextMeshProUGUI errorText;     // "틀린 암호입니다" 경고 텍스트

    [Header("비밀번호 설정")]
    public string correctPassword = "361025"; // 정답 암호 6자리 기획에 맞게 수정
    public string nextSceneName = "B2_Floor";  // 다음 층 씬 이름

    private string currentInput = "";
    private bool isKeypadActive = false;

    void Start()
    {
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        
        // ★ 수정: errorText 컴포넌트가 아닌, 그것이 붙은 gameObject를 비활성화합니다.
        if (errorText != null) errorText.gameObject.SetActive(false); 
    }

    void Update()
    {
        if (!isKeypadActive) return;

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

    public void OpenKeypad()
    {
        isKeypadActive = true;
        currentInput = "";
        UpdateDisplay();
        if (keypadUIPanel != null) keypadUIPanel.SetActive(true);
        if (errorText != null) errorText.gameObject.SetActive(false); // ★ 수정
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
        errorText.gameObject.SetActive(true); // ★ 수정 (스크린샷 아래쪽 에러 위치)
        yield return new WaitForSeconds(2f);
        errorText.gameObject.SetActive(false); // ★ 수정
    }
}
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections;
using Unity.Netcode;

[RequireComponent(typeof(ElevatorWrongCodeAlarm))]
public class ElevatorPanel : MonoBehaviour, IInteractable
{
    [Header("도어락 UI 연결")]
    [SerializeField] private GameObject keypadUIPanel;
    [SerializeField] private TextMeshProUGUI passwordText;
    [SerializeField] private TextMeshProUGUI errorText;

    [Header("3D 키패드 (지정하면 위 UI 대신 사용)")]
    [Tooltip("벽에 붙은 SF 터치 키패드. 카메라가 패널 앞으로 이동하고, 입력할 때마다 손이 터치 패드를 누르며, 오답이면 자리별로 초록/빨강을 표시합니다.")]
    [SerializeField] private ElevatorKeypad3D keypad3D;

    [Header("오답 경보")]
    [SerializeField] private ElevatorWrongCodeAlarm wrongCodeAlarm;

    [Header("비밀번호 및 탈출 설정")]
    [SerializeField] private string correctPassword = "361025";
    [SerializeField] private string nextSceneName = "B2_Floor";

    // ★ [하드코딩 방지] 퀘스트 순서가 바뀌면 인스펙터에서 이 숫자만 딸깍 고치면 됩니다.
    [Header("상호작용 조건 (QuestManager 연동)")]
    [Tooltip("엘리베이터를 열기 위해 현재 활성화되어야 하는 퀘스트 인덱스 번호")]
    [SerializeField] private int requiredQuestIndex = 4; 
    [Tooltip("권장: 순서가 바뀌어도 안전한 Quest ID. 비어 있을 때만 기존 인덱스를 사용합니다.")]
    [SerializeField] private string requiredQuestId;

    [Header("상호작용 HUD 문구")]
    [SerializeField] private string activeInteractionText = "키패드 열기 (E)";
    [SerializeField] private string lockedInteractionText = "아직 엘리베이터에 \n 접근할 수 없다.";

    private string currentInput = "";
    private bool isKeypadActive = false;
    private bool waitForInteractionKeyRelease;
    private PlayerInteraction savedPlayer;
    private Coroutine cursorRestoreRoutine;

    private void Awake()
    {
        ResolveWrongCodeAlarm();
    }

    private void Reset()
    {
        ResolveWrongCodeAlarm();
    }

    private void ResolveWrongCodeAlarm()
    {
        if (wrongCodeAlarm == null)
            wrongCodeAlarm = GetComponent<ElevatorWrongCodeAlarm>();

        if (wrongCodeAlarm == null)
        {
            Debug.LogError(
                "[ElevatorPanel] ElevatorWrongCodeAlarm component is missing. " +
                "Add it to the same keypad object.",
                this);
        }
    }

    void Start()
    {
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!isKeypadActive) return;

        // 키패드를 연 E 입력이 같은 프레임에 첫 글자로 전달되지 않도록 한다.
        if (waitForInteractionKeyRelease)
        {
            if (!Input.GetKey(KeyCode.E))
                waitForInteractionKeyRelease = false;

            return;
        }

        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                GameplayInputGate.ConsumeEscape(this);

            CloseKeypad();
            return;
        }

        // 숫자 입력 감지
        for (int i = 0; i <= 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
            {
                if (keypad3D != null) keypad3D.PressCharacter((char)('0' + i));
                AppendCharacter(i.ToString());
                return;
            }
        }

        // 알파벳 입력 감지
        for (KeyCode key = KeyCode.A; key <= KeyCode.Z; key++)
        {
            if (Input.GetKeyDown(key))
            {
                if (keypad3D != null) keypad3D.PressCharacter((char)('A' + (key - KeyCode.A)));
                AppendCharacter(key.ToString());
                return;
            }
        }

        if (Input.GetKeyDown(KeyCode.Backspace))
        {
            if (keypad3D != null) keypad3D.PressBackspace();
            RemoveLastCharacter();
        }

        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (keypad3D != null) keypad3D.PressSubmit();
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
        if (!isKeypadActive)
        {
            savedPlayer = null;
            return;
        }

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

        return !string.IsNullOrWhiteSpace(requiredQuestId)
            ? QuestManager.Instance.IsQuestActive(requiredQuestId)
            : QuestManager.Instance.IsQuestActive(requiredQuestIndex);
    }

    // ===== 내부 UI 제어 로직 =====

    public void OpenKeypad()
    {
        if (!GameplayInputGate.TryAcquire(this))
            return;

        isKeypadActive = true;
        waitForInteractionKeyRelease = true;
        currentInput = "";

        Camera playerCamera = savedPlayer != null ? savedPlayer.GetComponentInParent<Camera>() : null;
        if (keypad3D != null && keypad3D.BeginFocus(playerCamera, correctPassword.Length))
        {
            UpdateDisplay();
            return;
        }

        UpdateDisplay();
        if (keypadUIPanel != null) keypadUIPanel.SetActive(true);
        if (errorText != null) errorText.gameObject.SetActive(false);
    }

    public void CloseKeypad()
    {
        isKeypadActive = false;
        waitForInteractionKeyRelease = false;
        if (keypadUIPanel != null) keypadUIPanel.SetActive(false);
        if (keypad3D != null) keypad3D.EndFocus();
        GameplayInputGate.Release(this);
        
        if (savedPlayer != null)
        {
            savedPlayer.TogglePlayerControl(true);
            savedPlayer = null;
        }

        RestoreGameplayCursor();
        if (cursorRestoreRoutine != null)
            StopCoroutine(cursorRestoreRoutine);
        cursorRestoreRoutine = StartCoroutine(RestoreGameplayCursorAfterEscapeRelease());
    }

    private IEnumerator RestoreGameplayCursorAfterEscapeRelease()
    {
        // Unity Editor는 ESC가 눌려 있는 동안 CursorLockMode를 계속 해제할 수 있다.
        // 한 프레임만 기다리면 일반적인 키 입력 시간보다 짧으므로, 키를 완전히
        // 놓은 다음 프레임에 다시 잠가야 Editor 단독 실행에서도 유지된다.
        while (Input.GetKey(KeyCode.Escape))
            yield return null;

        yield return null;
        RestoreGameplayCursor();
        cursorRestoreRoutine = null;
    }

    private static void RestoreGameplayCursor()
    {
        // Pause/Setting은 자체적으로 메뉴 커서를 유지해야 한다.
        if (SettingManager.IsMenuOpen)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        cursorRestoreRoutine = null;
        GameplayInputGate.Release(this);
    }

    void AppendCharacter(string ch)
    {
        if (currentInput.Length >= correctPassword.Length) return;
        currentInput += ch;
        UpdateDisplay();
    }

    void RemoveLastCharacter()
    {
        if (currentInput.Length == 0) return;
        currentInput = currentInput.Substring(0, currentInput.Length - 1);
        UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (keypad3D != null) keypad3D.SetEntry(currentInput, correctPassword.Length);
        if (passwordText != null)
        {
            passwordText.text = currentInput;
            int remaining = Mathf.Max(0, correctPassword.Length - currentInput.Length);
            for (int i = 0; i < remaining; i++)
            {
                passwordText.text += " _";
            }
        }
    }

    void CheckPassword()
    {
        if (currentInput.Length != correctPassword.Length)
        {
            ShowError("암호를 끝까지 입력해 주세요.");
            if (keypad3D != null) keypad3D.ShowMessage("암호를 끝까지 입력해 주세요.", true);
            return;
        }

        if (currentInput == correctPassword)
        {
            Debug.Log("암호 일치! 다음 층으로 이동합니다.");
            if (keypad3D != null) keypad3D.ShowGranted("엘리베이터 사용이 승인되었습니다.");
            StartCoroutine(ApproveAndLoadRoutine());
        }
        else
        {
            Debug.Log("암호 불일치!");
            ShowError("틀린 암호입니다.");
            if (keypad3D != null)
            {
                // 자리별 판정: 맞은 글자는 초록, 틀린 글자는 빨강.
                var slotCorrect = new bool[correctPassword.Length];
                for (int i = 0; i < slotCorrect.Length; i++)
                    slotCorrect[i] = i < currentInput.Length && currentInput[i] == correctPassword[i];
                keypad3D.ShowDenied(currentInput, slotCorrect, "틀린 암호입니다. 경보가 울립니다!");
            }
            if (wrongCodeAlarm != null)
                wrongCodeAlarm.Trigger(savedPlayer);
            else
                Debug.LogError("[ElevatorPanel] Wrong-code alarm is not configured.", this);
            // Keep the current entry visible so Backspace can correct it.
        }
    }

    void ShowError(string message)
    {
        if (errorText == null) return;
        StopAllCoroutines();
        errorText.text = message;
        StartCoroutine(HideErrorRoutine());
    }

    IEnumerator HideErrorRoutine()
    {
        errorText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        errorText.gameObject.SetActive(false);
    }

    IEnumerator ApproveAndLoadRoutine()
    {
        isKeypadActive = false;

        if (errorText != null)
        {
            errorText.text = "엘리베이터 사용이 승인되었습니다.";
            errorText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(1.5f);

        NetworkManager manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening)
        {
            StarterAssets.PersonController playerController =
                savedPlayer != null ? savedPlayer.GetComponentInParent<StarterAssets.PersonController>() : null;

            if (playerController == null)
            {
                Debug.LogError("[ElevatorPanel] 씬 전환을 요청할 로컬 네트워크 플레이어를 찾지 못했습니다.", this);
                yield break;
            }

            playerController.RequestNetworkSceneLoad(nextSceneName);
            yield break;
        }

        SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }

    IEnumerator ShowErrorRoutine()
    {
        errorText.text = "틀린 암호입니다.";
        errorText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        errorText.gameObject.SetActive(false);
    }
}

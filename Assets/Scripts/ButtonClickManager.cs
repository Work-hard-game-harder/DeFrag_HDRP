using UnityEngine;
using UnityEngine.SceneManagement;

public class ButtonClickManager : MonoBehaviour
{
    private GameObject CodePanel;
    private GameObject PausePanel;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CodePanel = GameObject.Find("CodePanel");
        PausePanel = GameObject.Find("PausePanel");

        if (CodePanel != null)
            CodePanel.SetActive(false);

      if (PausePanel != null)
            PausePanel.SetActive(false);

    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && PausePanel != null)
        {
            PausePanel.SetActive(!PausePanel.activeSelf);
        }
    }
    public void SelectedLobbyScene()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SceneManager.LoadScene("CreateLobby", LoadSceneMode.Single);
    }

    public void SelectedCodePanel()
    {
        if (CodePanel != null)
        {
            AudioManager.Instance.PlaySFX("Button1");
            CodePanel.SetActive(true);
        }

    }
    public void ExitCodePanel()
    {
        if (CodePanel != null)
        {
            AudioManager.Instance.PlaySFX("Button1");
            CodePanel.SetActive(false);
        }
    }

    public void SelectedSettingPanel()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SettingManager.Instance?.OpenPanel();
    }

    public void ExitPausePanel()
    {
        if (PausePanel != null) {
            AudioManager.Instance.PlaySFX("Button1");
            PausePanel.SetActive(false);
        }
    }
    public void BackMainScene()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SceneManager.LoadScene("MainLobby", LoadSceneMode.Single);
    }
    public void QuitGame()
    {
        Application.Quit(); // 빌드된 게임에서 종료

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 에디터에서 종료 유도
#endif
    }
}

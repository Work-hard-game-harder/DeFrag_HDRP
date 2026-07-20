using UnityEngine;
using UnityEngine.SceneManagement;

public class ButtonClickManager : MonoBehaviour
{
     private GameObject CodePanel;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CodePanel = GameObject.Find("CodePanel");

        if (CodePanel != null)
            CodePanel.SetActive(false);

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

    public void QuitGame()
    {
        Application.Quit(); // 빌드된 게임에서 종료

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // 에디터에서 종료 유도
#endif
    }
}

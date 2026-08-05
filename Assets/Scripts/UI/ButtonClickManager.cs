using UnityEngine;
using UnityEngine.SceneManagement;

public class ButtonClickManager : MonoBehaviour
{
    [SerializeField] private GameObject codePanel;
    [SerializeField] private GameObject walkieImage;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (codePanel != null)
            codePanel.SetActive(false);

    }

    public void SelectedLobbyScene()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SceneManager.LoadScene("CreateLobby", LoadSceneMode.Single);
    }

    public void SelectedCodePanel()
    {
        if (codePanel != null)
        {
            AudioManager.Instance.PlaySFX("Button1");
            codePanel.SetActive(true);
        }

    }
    public void ExitCodePanel()
    {
        if (codePanel != null)
        {
            AudioManager.Instance.PlaySFX("Button1");
            codePanel.SetActive(false);
        }
    }

    public void SelectedSettingPanel()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SettingManager.Instance?.OpenPanel();
    }

    public void QuitGame()
    {
        Application.Quit(); // Quit in a player build.

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // Stop Play Mode in the Editor.
#endif
    }
}

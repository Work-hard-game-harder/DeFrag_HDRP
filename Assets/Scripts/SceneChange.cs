using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class SceneChange : MonoBehaviour
{
    [SerializeField] private string sceneName;
    [SerializeField] private Scene[] scenelist;
    [SerializeField] private GameObject floorCanvas;
    [SerializeField] private string mainLobbySceneName = "MainLobby";
    [Min(0f)] [SerializeField] private float sceneLoadDelay = 1f;

    private void Start()
    {
        if (floorCanvas != null)
        {
            floorCanvas.SetActive(false);
        }
    }

    public void SelectScene(string selectedSceneName)
    {
        AudioManager.Instance.PlaySFX("StageSelect");

        if (string.IsNullOrWhiteSpace(selectedSceneName))
        {
            Debug.LogWarning("선택할 씬이 설정되지 않았습니다.");
            return;
        }

        sceneName = selectedSceneName;
    }

    public void OnSelectedFloor()
    {
        AudioManager.Instance.PlaySFX("Button1");
        floorCanvas.SetActive(true);
    }

    public void OnDeselectedFloor()
    {
        AudioManager.Instance.PlaySFX("Button1");
        floorCanvas.SetActive(false);
        sceneName = string.Empty;
    }

    public void ChangeScene()
    {
        AudioManager.Instance.StopBGM();
        AudioManager.Instance.PlaySFX("StageCheck");

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            return;
        }

        StartCoroutine(LoadSceneWithDelay(sceneName, sceneLoadDelay));
    }

    private static IEnumerator LoadSceneWithDelay(string targetSceneName, float delay)
    {
        yield return new WaitForSeconds(delay);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (networkManager.IsServer)
            {
                networkManager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
            }

            yield break;
        }

        SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
    }

    public void BackMainScene()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SceneManager.LoadScene(mainLobbySceneName, LoadSceneMode.Single);
    }
}

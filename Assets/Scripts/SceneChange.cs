using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using System.Collections;

public class SceneChange : MonoBehaviour
{
    public string sceneName;
    public GameObject floorCanvas;

    public void Start()
    {
        if (floorCanvas != null)
            floorCanvas.SetActive(false);
    }
    public void SavedSceneName()
    {
        AudioManager.Instance.PlaySFX("StageSelect");
        if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
            return;

        sceneName = EventSystem.current.currentSelectedGameObject.name;
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
        AudioManager.Instance.StopBGM(); // BGM ²ô±â
        AudioManager.Instance.PlaySFX("StageCheck");
        if (string.IsNullOrEmpty(sceneName))
            return;

        Debug.Log("ÀÌµ¿ÇÏ·Á´Â ¾À ÀÌ¸§: " + sceneName);
        StartCoroutine(LoadSceneWithDelay(sceneName, 1f));
    }

    IEnumerator LoadSceneWithDelay(string sceneName, float delay)
    {
        yield return new WaitForSeconds(delay);
        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

    }

    public void BackMainScene()
    {
        AudioManager.Instance.PlaySFX("Button1");
        SceneManager.LoadScene("MainLobby", LoadSceneMode.Single);
    }
}
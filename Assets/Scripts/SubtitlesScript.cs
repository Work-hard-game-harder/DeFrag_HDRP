using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    private System.Action onFinished;
    public TextMeshProUGUI subtitlesText;
    public GameObject subtitlesPanel;
    public float subtitlesSpeed;
    private string[] subtitles;
    private int index = 0;

    private bool ignoreClick = false;

    void Start()
    {
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(false);
    }

    void Update()
    {
        if (subtitles == null) return;
        if (ignoreClick) return;
        if (Input.GetMouseButtonDown(0))
        {
            if (subtitlesText.text == subtitles[index])
                NextSubtitle();
            else
            {
                StopAllCoroutines();
                subtitlesText.text = subtitles[index];
            }
        }
    }

    // callback = null 기본값 추가, StopAllCoroutines 순서 수정
    public void PlaySubtitles(string[] newSubtitles, System.Action callback = null)
    {
        subtitles = newSubtitles;
        index = 0;
        onFinished = callback;
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(TypeLine());
        StartCoroutine(IgnoreClickThisFrame()); // 이번 프레임 클릭 무시
    }

    IEnumerator IgnoreClickThisFrame()
    {
        ignoreClick = true;
        yield return null; // 한 프레임 대기
        ignoreClick = false;
    }

    IEnumerator TypeLine()
    {
        foreach (char c in subtitles[index].ToCharArray())
        {
            subtitlesText.text += c;
            yield return new WaitForSeconds(subtitlesSpeed);
        }
    }

    void NextSubtitle()
    {
        if (index < subtitles.Length - 1)
        {
            index++;
            subtitlesText.text = string.Empty;
            StartCoroutine(TypeLine());
        }
        else
        {
            subtitlesPanel.SetActive(false);
            subtitles = null;
            onFinished?.Invoke();
        }
    }
}
using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    public TextMeshProUGUI subtitlesText;
    public GameObject subtitlesPanel;
    public float subtitlesSpeed;
    private string[] subtitles;
    private int index = 0;

    void Start()
    {
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(false);
    }

    void Update()
    {
        if (subtitles == null) return; // 자막 없으면 무시

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

    // 트리거에서 호출
    public void PlaySubtitles(string[] newSubtitles)
    {
        subtitles = newSubtitles;
        index = 0;
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(true);
        StartCoroutine(TypeLine());
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
        }
    }
}
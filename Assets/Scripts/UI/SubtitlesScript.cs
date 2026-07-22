using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    public TextMeshProUGUI subtitlesText;
    public GameObject subtitlesPanel;
    public float subtitlesSpeed;

    private Action onFinished;
    private string[] subtitles;
    private int index;
    private bool ignoreClick;

    private void Start()
    {
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(false);
    }

    private void Update()
    {
        if (subtitles == null || ignoreClick)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        if (subtitlesText.text == subtitles[index])
        {
            NextSubtitle();
        }
        else
        {
            StopAllCoroutines();
            subtitlesText.text = subtitles[index];
        }
    }

    public void PlaySubtitles(string[] newSubtitles, Action callback = null)
    {
        subtitles = newSubtitles;
        index = 0;
        onFinished = callback;
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(true);
        GameState.isCutscene = true;

        StopAllCoroutines();
        StartCoroutine(TypeLine());
        StartCoroutine(IgnoreClickThisFrame());
    }

    private IEnumerator IgnoreClickThisFrame()
    {
        ignoreClick = true;
        yield return null;
        ignoreClick = false;
    }

    private IEnumerator TypeLine()
    {
        foreach (char character in subtitles[index])
        {
            subtitlesText.text += character;
            yield return new WaitForSeconds(subtitlesSpeed);
        }
    }

    private void NextSubtitle()
    {
        if (index < subtitles.Length - 1)
        {
            index++;
            subtitlesText.text = string.Empty;
            StartCoroutine(TypeLine());
            return;
        }

        GameState.isCutscene = false;
        subtitlesPanel.SetActive(false);
        subtitles = null;

        Action finishedCallback = onFinished;
        onFinished = null;
        finishedCallback?.Invoke();
    }
}

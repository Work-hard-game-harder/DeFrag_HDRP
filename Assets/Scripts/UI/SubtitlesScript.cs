using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class SubtitlesScript : MonoBehaviour
{
    public TextMeshProUGUI subtitlesText;
    public GameObject subtitlesPanel;
    public float subtitlesSpeed;

    [Header("Typewriter Audio")]
    [SerializeField] private AudioSource typewriterSource;
    [SerializeField] private AudioClip[] typewriterClips;
    [Range(0f, 1f)] [SerializeField] private float typewriterVolume = 0.65f;
    [SerializeField] private Vector2 typewriterPitchRange = new Vector2(0.96f, 1.04f);

    private Action onFinished;
    private string[] subtitles;
    private int index;
    private bool ignoreClick;

    private void Start()
    {
        EnsureTypewriterSource();
        subtitlesText.text = string.Empty;
        subtitlesPanel.SetActive(false);
    }

    private void EnsureTypewriterSource()
    {
        if (typewriterSource == null)
            typewriterSource = gameObject.AddComponent<AudioSource>();

        typewriterSource.playOnAwake = false;
        typewriterSource.loop = false;
        typewriterSource.spatialBlend = 0f;
        typewriterSource.dopplerLevel = 0f;
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
            StopTypewriterSound();
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
        StopTypewriterSound();
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
        StartTypewriterSound();
        foreach (char character in subtitles[index])
        {
            subtitlesText.text += character;
            yield return new WaitForSeconds(subtitlesSpeed);
        }
        StopTypewriterSound();
    }

    private void StartTypewriterSound()
    {
        if (typewriterSource == null || typewriterClips.Length == 0)
            return;

        typewriterSource.Stop();
        typewriterSource.pitch = UnityEngine.Random.Range(
            typewriterPitchRange.x,
            typewriterPitchRange.y);
        typewriterSource.clip = typewriterClips[
            UnityEngine.Random.Range(0, typewriterClips.Length)];
        typewriterSource.volume = typewriterVolume;
        typewriterSource.loop = true;
        typewriterSource.Play();
    }

    private void StopTypewriterSound()
    {
        if (typewriterSource == null)
            return;

        typewriterSource.Stop();
        typewriterSource.loop = false;
        typewriterSource.clip = null;
    }

    private void OnValidate()
    {
        if (typewriterPitchRange.x > typewriterPitchRange.y)
            typewriterPitchRange = new Vector2(
                typewriterPitchRange.y,
                typewriterPitchRange.x);
    }

    private void OnDisable()
    {
        StopTypewriterSound();
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
        StopTypewriterSound();
        subtitlesPanel.SetActive(false);
        subtitles = null;

        Action finishedCallback = onFinished;
        onFinished = null;
        finishedCallback?.Invoke();
    }
}

using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Video;

[DisallowMultipleComponent]
public sealed class HintSequencePresentation : MonoBehaviour
{
    [Header("Screen")]
    [Tooltip("시퀀스 동안에만 표시할 Canvas, 화면, 카메라 루트입니다.")]
    [SerializeField] private GameObject presentationRoot;

    [Header("Optional playback")]
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private PlayableDirector playableDirector;
    [SerializeField] private Animator animator;
    [SerializeField] private string animatorTrigger;

    [Header("Events")]
    [SerializeField] private UnityEvent onPresentationStarted;
    [SerializeField] private UnityEvent onPresentationStopped;

    private void Awake()
    {
        if (presentationRoot != null)
            presentationRoot.SetActive(false);
    }

    public void Play()
    {
        if (presentationRoot != null)
            presentationRoot.SetActive(true);

        if (videoPlayer != null)
        {
            videoPlayer.Stop();
            videoPlayer.Play();
        }

        if (playableDirector != null)
        {
            playableDirector.time = 0d;
            playableDirector.Play();
        }

        if (animator != null && !string.IsNullOrWhiteSpace(animatorTrigger))
            animator.SetTrigger(animatorTrigger);

        onPresentationStarted?.Invoke();
    }

    public void Stop()
    {
        if (videoPlayer != null)
            videoPlayer.Stop();
        if (playableDirector != null)
            playableDirector.Stop();

        onPresentationStopped?.Invoke();

        if (presentationRoot != null)
            presentationRoot.SetActive(false);
    }
}

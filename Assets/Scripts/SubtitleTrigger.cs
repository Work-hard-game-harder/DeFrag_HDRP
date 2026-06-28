using UnityEngine;

public class SubtitleTrigger : MonoBehaviour
{
    public SubtitlesScript subtitlesScript; // SubtitleBox 연결
    public string[] mySubtitles;           // 이 트리거만의 자막 내용
    private bool hasTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;
        if (other.CompareTag("Player"))
        {
            hasTriggered = true;
            subtitlesScript.PlaySubtitles(mySubtitles); // 자막 실행
        }
    }
}
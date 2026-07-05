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
    private void OnMouseDown()
    {
        if (hasTriggered) return;
        if (CompareTag("Item"))
        {
            hasTriggered = true;
            subtitlesScript.PlaySubtitles(mySubtitles);
            gameObject.SetActive(false); // 자막 끝나면 비활성화
        }
    }
}
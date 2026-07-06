using EasyPeasyFirstPersonController;
using UnityEngine;

public class SubtitleTrigger : MonoBehaviour
{
    public SubtitlesScript subtitlesScript; // SubtitleBox 연결
    public string[] mySubtitles;           // 이 트리거만의 자막 내용
    private bool hasTriggered = false;
    public GameObject wakietakie; // 아이템 클릭 시 활성화할 오브젝트

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
            FirstPersonController player = FindAnyObjectByType<FirstPersonController>();
            if (player != null) player.PickUpWakieTakie();
            gameObject.SetActive(false);
            subtitlesScript.PlaySubtitles(mySubtitles);
        }
    }
}
using EasyPeasyFirstPersonController;
using UnityEngine;

public class SubtitleTrigger : MonoBehaviour
{
    public SubtitlesScript subtitlesScript; // SubtitleBox ����
    public string[] mySubtitles;           // �� Ʈ���Ÿ��� �ڸ� ����
    private bool hasTriggered = false;
    public GameObject wakietakie; // ������ Ŭ�� �� Ȱ��ȭ�� ������Ʈ

    private void OnTriggerEnter(Collider other)
    {
        if (hasTriggered) return;
        if (other.CompareTag("Player"))
        {
            hasTriggered = true;
            subtitlesScript.PlaySubtitles(mySubtitles); // �ڸ� ����
        }
    }

    /*
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

    */
}
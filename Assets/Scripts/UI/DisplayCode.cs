using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections; // Coroutine을 위해 필요

public class DisplayCode : MonoBehaviour
{
    public TextMeshProUGUI lobbyCodeText;

    void Start()
    {
        if (lobbyCodeText != null && !string.IsNullOrEmpty(LobbyManager.SavedJoinCode))
        {
            lobbyCodeText.text = "Code: " + LobbyManager.SavedJoinCode;
        }
        else if (lobbyCodeText != null)
        {
            lobbyCodeText.text = "코드를 불러올 수 없습니다.";
        }
        if (NetworkManager.Singleton.IsHost)
        {
            StartCoroutine(WaitAndSetPosition());
        }
    }

    IEnumerator WaitAndSetPosition()
    {
        // 캐릭터 오브젝트가 생성될 때까지 최대 1초 정도 대기 (안전장치)
        float timer = 0;
        while (NetworkManager.Singleton.LocalClient.PlayerObject == null && timer < 1f)
        {
            timer += Time.deltaTime;
            yield return null;
        }
        // 이제 위치를 옮깁니다.
        SetPlayerPosition();
    }

    private void SetPlayerPosition()
    {
        GameObject spawnPointHost = GameObject.Find("SpawnPoint_Host");
        if (spawnPointHost != null)
        {
            var player = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (player != null)
            {
                player.transform.position = spawnPointHost.transform.position;
                player.transform.rotation = spawnPointHost.transform.rotation;
                Debug.Log("호스트가 지정된 스폰 포인트로 이동되었습니다.");
            }
        }
        else
        {
            Debug.LogError("SpawnPoint_Host 오브젝트를 찾을 수 없습니다!");
        }
    }
}
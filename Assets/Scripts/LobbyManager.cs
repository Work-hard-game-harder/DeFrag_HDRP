using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Netcode;
using UnityEngine;
using TMPro;
using System;

public class LobbyManager : MonoBehaviour
{
    public TextMeshProUGUI joinCodeText;
    public static string SavedJoinCode;

    async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"서비스 초기화 실패: {e.Message}");
        }
    }



    public async void StartHostWithRelay()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            SavedJoinCode = joinCode;
            if (joinCodeText != null) joinCodeText.text = "CODE: " + joinCode;
            var transport = NetworkManager.Singleton.GetComponent("UnityTransport");

            if (transport != null)
            {
                transport.GetType().GetMethod("SetHostRelayData").Invoke(transport, new object[] {
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData,
                    true
                });
            }
            else
            {
                Debug.LogError("NetworkManager에 UnityTransport 컴포넌트가 없습니다!");
                return;
            }
            if (NetworkManager.Singleton.StartHost())
            {
                Debug.Log("호스트 시작 성공!");
                NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        catch (RelayServiceException e)
        {
            Debug.LogError($"로비 생성 실패: {e}");
        }
        catch (Exception e)
        {
            Debug.LogError($"알 수 없는 오류 발생: {e.Message}");
        }
    }
}
using UnityEngine;
using TMPro;

public class NetworkRoomManager : MonoBehaviour
{

    //멀티 플레이에서 모든 플레이어가 단서 카운트롤 공유하는 것을 관리하는 싱글톤 매니저입니다.
    //필요하지 않다면 사용하지 않아도 좋습니다.
    public static NetworkRoomManager Instance;

    [Header("UI")]
    public TextMeshProUGUI globalClueText;

    [Header("게임 데이터")]
    // 포톤이나 미러를 쓸 경우 이 변수는 [SyncVar] 혹은 RPC를 통해 서버가 동기화해야 합니다.
    private int currentClues = 0; 
    private int totalCluesNeeded = 6;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    // 플레이어가 아이템 획득 성공 시 서버에 요청하는 함수 (RPC 개념)
    public void PickUpItem(InteractableItem item)
    {
        // 호스트/서버 측 검증: 이미 먹은 아이템이 아닐 때만 실행
        if (item.isInteracted) return; 

        // 멀티플레이어 전원에게 이 오브젝트가 파괴/비활성화되었음을 동기화
        item.isInteracted = true;
        item.gameObject.SetActive(false); 

        // 전원의 단서 카운트 증가 및 UI 업데이트
        UpdateClueCount();
    }

    void UpdateClueCount()
    {
        currentClues++;
        globalClueText.text = $"단서를 모으시오 {currentClues} / {totalCluesNeeded}";

        if (currentClues >= totalCluesNeeded)
        {
            UnlockElevator();
        }
    }

    void UnlockElevator()
    {
        // 엘레베이터 코드 입력 패널 활성화 RPC 호출
        Debug.Log("모든 단서 획득! 엘레베이터 잠금 해제 가능.");
    }
}
using UnityEngine;
using TMPro;

// 1. 이 층에서 일어날 수 있는 퀘스트 단계를 정의 (층마다 인스펙터에서 관리)
public enum QuestStep
{
    None,
    FindElevator,     // 1단계: 엘레베이터 단서 찾기
    InputKeypad,      // 2단계: 키패드 암호 입력
    EscapeFloor,      // 3단계: 탈출하기
    // 필요 시 4, 5, 6... 10단계까지 층의 기획에 맞게 확장 가능
}

// 2. 인스펙터창에서 각 단계별 텍스트와 UI 패널을 매칭하기 위한 구조체
[System.Serializable]
public struct QuestStepData
{
    public QuestStep stepType;            // 어떤 단계인가?
    public GameObject questUIPanel;       // 해당 단계에서 켜질 UI 부모 오브젝트 (공용이라면 같은 걸 넣어도 됨)
    public TextMeshProUGUI progressText;  // 내용을 교체할 TMP 텍스트 컴포넌트
    
    [TextArea(1, 3)]
    public string targetMessage;          // 화면에 띄울 미션 문구 (예: "단서를 모으시오")
}

public class QuestManager : MonoBehaviour
{
    // 층마다 고유한 static Instance를 가집니다. (씬이 바뀔 때마다 해당 층의 매니저가 할당됨)
    public static QuestManager Instance;

    [Header("이 층의 퀘스트 순서 세팅")]
    public QuestStepData[] floorQuestSteps; 

    [Header("1단계(단서 파밍)용 설정")]
    public int totalCluesNeeded = 6;       // 이 층에서 필요한 단서 개수

    private int currentStepIndex = -1;     // 현재 몇 번째 퀘스트 데이터가 돌고 있는지
    private int currentClues = 0;          // 현재 모은 단서 수
    private bool isQuestStarted = false;   // 최초 스토리 트리거 작동 여부

    void Awake()
    {
        // 층이 바뀔 때 현재 씬에 있는 매니저를 싱글톤 주체로 세팅
        Instance = this;
    }

    void Start()
    {
        // 시작할 때는 인스펙터에 등록된 모든 퀘스트 UI 패널을 꺼둡니다.
        HideAllQuestUI();
    }

    // [1단계 시작] 팀원의 스토리 트리거(투명 벽 등)를 밟았을 때 호출됨
    public void StartFloorQuest()
    {
        if (isQuestStarted) return;
        isQuestStarted = true;

        // 첫 번째 퀘스트 단계(Index 0)를 시작합니다.
        SetQuestStep(0);
    }

    // 지정한 인덱스의 퀘스트 단계를 활성화하는 함수
    void SetQuestStep(int index)
    {
        if (index < 0 || index >= floorQuestSteps.Length) return;

        HideAllQuestUI(); // 기존 UI 정리
        currentStepIndex = index;
        
        // 현재 단계의 UI 켜기
        var currentData = floorQuestSteps[currentStepIndex];
        if (currentData.questUIPanel != null) currentData.questUIPanel.SetActive(true);

        // 첫 번째 단계가 '엘레베이터 단서 찾기(FindElevator)'라면 카운트 표기 기능 작동
        if (currentData.stepType == QuestStep.FindElevator)
        {
            currentClues = 0;
            UpdateClueUI();
        }
        else
        {
            // 카운트가 필요 없는 일반 텍스트 미션(예: 키패드 입력)은 등록된 문구 그대로 출력
            if (currentData.progressText != null)
            {
                currentData.progressText.text = currentData.targetMessage;
            }
        }
    }

    // 플레이어가 단서를 먹을 때마다 호출 (PlayerInteraction에서 호출)
    public void AddClue()
    {
        if (!isQuestStarted || currentStepIndex == -1) return;

        // 현재 단계가 FindElevator일 때만 단서 카운트 증가 연산 수행
        if (floorQuestSteps[currentStepIndex].stepType == QuestStep.FindElevator)
        {
            currentClues++;

            if (currentClues >= totalCluesNeeded)
            {
                // 단서를 다 모았다면 다음 단계(Index 1: 키패드 복귀)로 자동 전환!
                GoToNextStep();
            }
            else
            {
                UpdateClueUI();
            }
        }
    }

    // 단서 카운트 실시간 업데이트
    void UpdateClueUI()
    {
        var data = floorQuestSteps[currentStepIndex];
        if (data.progressText != null)
        {
            data.progressText.text = $"{data.targetMessage} {currentClues} / {totalCluesNeeded}";
        }
    }

    // 다음 단계 퀘스트로 강제 전환 (단서를 다 모았거나 외부 조건 만족 시)
    public void GoToNextStep()
    {
        int nextIndex = currentStepIndex + 1;
        
        if (nextIndex < floorQuestSteps.Length)
        {
            SetQuestStep(nextIndex);
            Debug.Log($"{gameObject.name} : 다음 퀘스트 단계 진입 - 인덱스 {nextIndex}");
        }
        else
        {
            // 더 이상 다음 단계가 없다면 이 층의 모든 퀘스트 완료 처리
            Debug.Log($"{gameObject.name} : 이 층의 모든 퀘스트 클리어!");
            HideAllQuestUI();
        }
    }

    void HideAllQuestUI()
    {
        foreach (var step in floorQuestSteps)
        {
            if (step.questUIPanel != null) step.questUIPanel.SetActive(false);
        }
    }
}
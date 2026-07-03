using UnityEngine;
using TMPro;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [Header("공용 Quest UI 연결")]
    public GameObject questContainerPanel;   
    public TextMeshProUGUI questProgressText; 

    [Header("1단계(단서 파밍)용 설정")]
    public int totalCluesNeeded = 6;         

    [HideInInspector] public bool isClueQuestActive = false; 
    private int currentStepIndex = 0;        
    private int currentClues = 0;            

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (questContainerPanel != null) questContainerPanel.SetActive(true);
        SetQuestStep(0);
    }

    public void SetQuestStep(int index)
    {
        currentStepIndex = index;
        
        if (currentStepIndex == 1)
        {
            isClueQuestActive = true;
            UpdateClueUI();
        }
        else
        {
            isClueQuestActive = false;
            UpdateGeneralUI();
        }
    }

    public void AddClue()
    {
        currentClues++;
        if (currentClues >= totalCluesNeeded)
        {
            SetQuestStep(2); // 6개 다 모으면 엘레베이터 복귀 단계 전환
        }
        else
        {
            UpdateClueUI();
        }
    }

    void UpdateClueUI()
    {
        if (questProgressText != null)
        {
            questProgressText.text = $"엘레베이터 작동을 위한 단서를 모으시오 {currentClues} / {totalCluesNeeded}";
        }
    }

    void UpdateGeneralUI()
    {
        if (questProgressText == null) return;

        switch (currentStepIndex)
        {
            case 0:
                questProgressText.text = "1층을 진입할 방법을 찾는다.";
                break;
            case 2:
                questProgressText.text = "<color=#00FF00>엘레베이터로 돌아가 지하 2층으로 진입하기</color>";
                break;
        }
    }
}
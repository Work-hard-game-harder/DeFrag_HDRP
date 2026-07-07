using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [Header("UI 연결")]
    public GameObject questContainerPanel;   
    public TextMeshProUGUI questProgressText; 

    [Header("퀘스트 리스트 (순서대로 진행됨)")]
    // ★ 인스펙터 창에서 원하는 만큼 원소를 추가하고 마우스로 드래그해서 순서를 바꿀 수 있습니다!
    public List<QuestStep> questList = new List<QuestStep>();

    private int currentStepIndex = 0;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        if (questContainerPanel != null) questContainerPanel.SetActive(true);
        UpdateQuestUI();
    }

    // 현재 진행 중인 퀘스트 단계를 반환하는 프로퍼티
    public QuestStep CurrentStep
    {
        get
        {
            if (currentStepIndex >= 0 && currentStepIndex < questList.Count)
                return questList[currentStepIndex];
            return null;
        }
    }

    // 외부(단서 아이템 등)에서 카운트를 올릴 때 쓰는 공용 함수
    public void ProgressActiveQuest(int amount = 1)
    {
        if (CurrentStep == null) return;

        CurrentStep.currentCount += amount;
        Debug.Log($"퀘스트 진행 중: {CurrentStep.questTitle} ({CurrentStep.currentCount}/{CurrentStep.targetCount})");

        // 현재 단계가 완료되었다면 다음 단계로
        if (CurrentStep.IsCompleted())
        {
            NextQuestStep();
        }
        else
        {
            UpdateQuestUI();
        }
    }

    // 다음 퀘스트 단계로 넘어가는 로직
    void NextQuestStep()
    {
        currentStepIndex++;

        if (currentStepIndex >= questList.Count)
        {
            // 모든 퀘스트가 끝났을 때
            if (questProgressText != null) questProgressText.text = "<color=#00FF00>모든 임무 완수</color>";
            Debug.Log("게임 내 모든 퀘스트 체인이 종료되었습니다.");
        }
        else
        {
            Debug.Log($"다음 퀘스트 돌입: {CurrentStep.questTitle}");
            UpdateQuestUI();
        }
    }

    // 화면에 퀘스트 정보를 갱신하는 함수
    public void UpdateQuestUI()
    {
        if (questProgressText == null || CurrentStep == null) return;

        // 목표 개수가 1개보다 많다면 (예: 단서 6개 모으기) 카운트 숫자 표기 추가
        if (CurrentStep.targetCount > 1)
        {
            questProgressText.text = $"{CurrentStep.questTitle} ({CurrentStep.currentCount} / {CurrentStep.targetCount})";
        }
        else
        {
            questProgressText.text = CurrentStep.questTitle;
        }
    }

    // 엘레베이터 등에서 "단서 다 모았니?"라고 물어볼 때 검사하는 함수
    public bool IsElevatorReady()
    {
        // 팁: 기획상 리스트의 '맨 마지막 퀘스트' 단계가 엘레베이터 진입 단계라고 가정하거나,
        // 특정 타이틀명을 검사할 수도 있습니다. 여기서는 마지막 퀘스트 단계에 도달했는지를 체크합니다.
        if (CurrentStep == null) return true; // 모든 퀘스트가 끝났다면 오픈
        
        // 현재 인덱스가 마지막 인덱스라면 단서를 다 모았다는 뜻!
        return currentStepIndex == questList.Count - 1; 
    }

    // 기존 스크립트들과의 하위 호환성을 위한 껍데기 함수 (에러 방지용)
    public bool isClueQuestActive 
    {
        get 
        {
            // 현재 퀘스트 제목에 '단서'가 포함되어 있다면 활성화된 것으로 판단
            if (CurrentStep == null) return false;
            return CurrentStep.questTitle.Contains("단서");
        }
    }
    public void AddClue() { ProgressActiveQuest(1); }
}
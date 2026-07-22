using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [Header("UI 연결")]
    public GameObject questContainerPanel;
    public TextMeshProUGUI questProgressText;

    [Header("순서대로 진행할 퀘스트")]
    public List<QuestStep> questList = new List<QuestStep>();

    private int currentStepIndex;
    private int pendingStepIndex = -1;

    public Action onQuestStepChanged;

    public int GetCurrentStepIndex() => currentStepIndex;
    public bool IsWaitingForSubtitleReveal => pendingStepIndex >= 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (questList.Count == 0)
        {
            SetQuestUIVisible(false);
            return;
        }

        // 첫 퀘스트 역시 씬의 첫 SubtitleTrigger 자막이 끝난 뒤 공개합니다.
        pendingStepIndex = 0;
        SetQuestUIVisible(false);
        onQuestStepChanged?.Invoke();
    }

    public QuestStep CurrentStep
    {
        get
        {
            if (currentStepIndex >= 0 && currentStepIndex < questList.Count)
                return questList[currentStepIndex];

            return null;
        }
    }

    public void ProgressActiveQuest(int amount = 1)
    {
        // 다음 퀘스트가 자막 공개를 기다리는 동안 중복 진행을 막습니다.
        if (CurrentStep == null || IsWaitingForSubtitleReveal)
            return;

        CurrentStep.currentCount += amount;
        Debug.Log($"퀘스트 진행 중: {CurrentStep.questTitle} ({CurrentStep.currentCount}/{CurrentStep.targetCount})");

        if (CurrentStep.IsCompleted())
            PrepareNextQuest();
        else
            UpdateQuestUI();
    }

    private void PrepareNextQuest()
    {
        int nextStepIndex = currentStepIndex + 1;

        if (nextStepIndex >= questList.Count)
        {
            CompleteAllQuests();
            return;
        }

        // 다음 퀘스트는 논리적으로도 아직 활성화하지 않고 대기시킵니다.
        // 이후 기존 SubtitleTrigger의 자막이 끝나면 RevealPendingQuestAfterSubtitle이 호출됩니다.
        pendingStepIndex = nextStepIndex;
        SetQuestUIVisible(false);
        // 현재 퀘스트가 완료된 즉시 Barrier 등의 구독자에게 비활성화를 알립니다.
        onQuestStepChanged?.Invoke();
        Debug.Log($"다음 퀘스트가 자막 공개를 기다리는 중: {questList[pendingStepIndex].questTitle}");
    }

    public void RevealPendingQuestAfterSubtitle()
    {
        // 퀘스트 완료와 무관한 일반 자막 트리거에서는 아무 작업도 하지 않습니다.
        if (!IsWaitingForSubtitleReveal)
            return;

        currentStepIndex = pendingStepIndex;
        pendingStepIndex = -1;

        UpdateQuestUI();
        SetQuestUIVisible(true);
        onQuestStepChanged?.Invoke();
        Debug.Log($"자막 종료 후 다음 퀘스트 공개: {CurrentStep.questTitle}");
    }

    private void CompleteAllQuests()
    {
        currentStepIndex = questList.Count;
        pendingStepIndex = -1;
        SetQuestUIVisible(false);

        if (questProgressText != null)
            questProgressText.text = string.Empty;

        onQuestStepChanged?.Invoke();
        Debug.Log("게임 내 모든 퀘스트 체인이 종료되었습니다.");
    }

    private void SetQuestUIVisible(bool visible)
    {
        if (questContainerPanel != null)
            questContainerPanel.SetActive(visible);
    }

    public void UpdateQuestUI()
    {
        if (questProgressText == null || CurrentStep == null)
            return;

        questProgressText.text = CurrentStep.targetCount > 1
            ? $"{CurrentStep.questTitle} ({CurrentStep.currentCount} / {CurrentStep.targetCount})"
            : CurrentStep.questTitle;
    }

    public bool IsElevatorReady()
    {
        if (CurrentStep == null)
            return true;

        return !IsWaitingForSubtitleReveal && currentStepIndex == questList.Count - 1;
    }

    public bool isClueQuestActive
    {
        get
        {
            if (CurrentStep == null || IsWaitingForSubtitleReveal)
                return false;

            return CurrentStep.questTitle.Contains("단서");
        }
    }

    public void AddClue() => ProgressActiveQuest(1);

    public bool IsQuestActive(int questIndex)
    {
        return !IsWaitingForSubtitleReveal && currentStepIndex == questIndex;
    }

    public bool IsQuestActive(string questId)
    {
        // QuestStep에 안정적인 questId가 추가되기 전까지 문자열 ID 조회는 지원하지 않습니다.
        return false;
    }
}

using System;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [Header("UI 연결")]
    public GameObject questContainerPanel;
    public TextMeshProUGUI questProgressText;

    [Header("순서대로 진행할 퀘스트")]
    public List<QuestStep> questList = new();

    private readonly Dictionary<int, HashSet<string>> acceptedSources = new();
    private int currentStepIndex;
    private int pendingStepIndex = -1;

    public Action onQuestStepChanged;

    public bool IsInitialized { get; private set; }
    public int GetCurrentStepIndex() => currentStepIndex;
    public bool IsWaitingForSubtitleReveal => pendingStepIndex >= 0;
    public int CurrentCount => CurrentStep?.currentCount ?? 0;
    public QuestStep CurrentStep => GetStep(currentStepIndex);
    private QuestStep PendingStep => GetStep(pendingStepIndex);

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("[QuestManager] 씬에 QuestManager가 두 개 이상 존재합니다.", this);
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (questList.Count == 0)
        {
            IsInitialized = true;
            SetQuestUIVisible(false);
            return;
        }

        ValidateQuestConfiguration();
        IsInitialized = true;
        PrepareStepForReveal(0);
    }

    public bool TryCompleteThroughForStoryDebugServer(string completedThroughQuestId)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        NetworkManager networkManager = NetworkManager.Singleton;
        if (!IsInitialized ||
            (networkManager != null && networkManager.IsListening && !networkManager.IsServer) ||
            string.IsNullOrWhiteSpace(completedThroughQuestId))
            return false;

        int completedIndex = questList.FindIndex(step =>
            step != null && string.Equals(
                step.questId,
                completedThroughQuestId.Trim(),
                StringComparison.OrdinalIgnoreCase));
        if (completedIndex < 0)
        {
            Debug.LogError(
                $"[QuestManager] 디버그 체크포인트 Quest ID를 찾지 못했습니다: {completedThroughQuestId}",
                this);
            return false;
        }

        acceptedSources.Clear();
        for (int i = 0; i <= completedIndex; i++)
        {
            QuestStep completedStep = GetStep(i);
            if (completedStep != null)
                completedStep.currentCount = Mathf.Max(1, completedStep.targetCount);
        }

        int nextStepIndex = completedIndex + 1;
        if (nextStepIndex >= questList.Count)
            CompleteAllQuests();
        else
        {
            currentStepIndex = nextStepIndex;
            pendingStepIndex = -1;
            QuestStep nextStep = CurrentStep;
            nextStep.currentCount = 0;
            UpdateQuestUI();
            SetQuestUIVisible(true);
            onQuestStepChanged?.Invoke();
            Debug.Log(
                $"[QuestManager] 디버그 체크포인트 적용: {completedThroughQuestId} 완료, " +
                $"{nextStep.questId} 활성화.",
                this);
        }

        if (networkManager != null && networkManager.IsListening && networkManager.IsServer)
            BroadcastSharedSnapshotFromServer();
        return true;
#else
        return false;
#endif
    }

    public bool ReportProgress(string signal, string sourceId, int amount = 1)
    {
        QuestStep step = CurrentStep;
        if (!CanAcceptProgress(step, signal, amount))
            return false;

        if (step.progressScope == QuestProgressScope.LocalPlayer)
            return ApplyValidatedProgress(signal, sourceId, amount);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return ApplyValidatedProgress(signal, sourceId, amount);

        if (networkManager.IsServer)
        {
            bool changed = TryReportSharedProgressOnServer(signal, sourceId, amount);
            if (changed)
                BroadcastSharedSnapshotFromServer();
            return changed;
        }

        PersonController relay = GetLocalPlayerRelay(networkManager);
        if (relay == null)
        {
            Debug.LogError("[QuestManager] 공용 퀘스트 요청을 보낼 로컬 플레이어를 찾지 못했습니다.", this);
            return false;
        }

        relay.RequestSharedQuestProgress(signal, sourceId ?? string.Empty, amount);
        return true;
    }

    public bool TryReportSharedProgressOnServer(string signal, string sourceId, int amount)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return false;

        QuestStep step = CurrentStep;
        if (step == null || step.progressScope != QuestProgressScope.SharedParty)
            return false;

        return ApplyValidatedProgress(signal, sourceId, amount);
    }

    public void ApplySharedSnapshot(int stepIndex, int waitingStepIndex, int currentCount)
    {
        if (stepIndex == questList.Count && waitingStepIndex < 0)
        {
            CompleteAllQuests();
            return;
        }

        QuestStep authoritativeStep = GetStep(stepIndex);
        if (authoritativeStep == null ||
            authoritativeStep.progressScope != QuestProgressScope.SharedParty)
            return;

        currentStepIndex = stepIndex;
        pendingStepIndex = waitingStepIndex;
        MarkStepsBeforeCompleted(stepIndex);
        authoritativeStep.currentCount = Mathf.Max(0, currentCount);

        if (IsWaitingForSubtitleReveal)
            SetQuestUIVisible(false);
        else
        {
            UpdateQuestUI();
            SetQuestUIVisible(true);
        }

        onQuestStepChanged?.Invoke();
    }

    public void RevealPendingQuestAfterSubtitle()
    {
        QuestStep pending = PendingStep;
        if (pending == null)
            return;

        if (pending.progressScope == QuestProgressScope.LocalPlayer)
        {
            RevealPendingLocally();
            return;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            RevealPendingLocally();
            return;
        }

        if (networkManager.IsServer)
        {
            if (TryRevealSharedPendingOnServer())
                BroadcastSharedSnapshotFromServer();
            return;
        }

        PersonController relay = GetLocalPlayerRelay(networkManager);
        if (relay != null)
            relay.RequestSharedQuestReveal();
        else
            Debug.LogError("[QuestManager] 공용 퀘스트 공개 요청을 보낼 로컬 플레이어를 찾지 못했습니다.", this);
    }

    public bool TryRevealSharedPendingOnServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return false;
        if (PendingStep == null || PendingStep.progressScope != QuestProgressScope.SharedParty)
            return false;

        RevealPendingLocally();
        return true;
    }

    public void BroadcastSharedSnapshotFromServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            return;

        foreach (NetworkClient client in networkManager.ConnectedClientsList)
        {
            PersonController relay = client.PlayerObject != null
                ? client.PlayerObject.GetComponent<PersonController>()
                : null;
            if (relay == null)
                continue;

            relay.BroadcastSharedQuestSnapshotFromServer(
                currentStepIndex,
                pendingStepIndex,
                CurrentCount);
            return;
        }

        Debug.LogWarning("[QuestManager] 공용 퀘스트 상태를 방송할 플레이어 릴레이가 없습니다.", this);
    }

    [Obsolete("Use ReportProgress(signal, sourceId, amount).")]
    public void ProgressActiveQuest(int amount = 1)
    {
        Debug.LogWarning(
            "[QuestManager] 신호 없는 ProgressActiveQuest 호출은 거부되었습니다. ReportProgress를 사용하세요.",
            this);
    }

    private bool CanAcceptProgress(QuestStep step, string signal, int amount)
    {
        if (step == null || IsWaitingForSubtitleReveal || amount <= 0 || step.persistUntilSceneChange)
            return false;
        if (step.AcceptsSignal(signal))
            return true;

        Debug.Log(
            $"[QuestManager] '{signal}' 신호는 현재 퀘스트 '{step.questId}'의 목표가 아니므로 무시합니다.",
            this);
        return false;
    }

    private bool ApplyValidatedProgress(string signal, string sourceId, int amount)
    {
        QuestStep step = CurrentStep;
        if (!CanAcceptProgress(step, signal, amount))
            return false;

        string normalizedSource = NormalizeSource(sourceId, signal);
        if (step.rejectDuplicateSources && !GetAcceptedSources(currentStepIndex).Add(normalizedSource))
            return false;

        step.currentCount = Mathf.Min(step.currentCount + amount, Mathf.Max(1, step.targetCount));
        Debug.Log(
            $"[QuestManager] {step.questId}: {step.currentCount}/{Mathf.Max(1, step.targetCount)} " +
            $"(signal={signal}, source={normalizedSource}, scope={step.progressScope})",
            this);

        if (step.IsCompleted())
            PrepareNextQuest();
        else
            UpdateQuestUI();
        return true;
    }

    private void PrepareNextQuest()
    {
        int nextStepIndex = currentStepIndex + 1;
        if (nextStepIndex >= questList.Count)
        {
            CompleteAllQuests();
            return;
        }

        PrepareStepForReveal(nextStepIndex);
    }

    private void PrepareStepForReveal(int stepIndex)
    {
        QuestStep step = GetStep(stepIndex);
        if (step == null)
            return;

        pendingStepIndex = stepIndex;
        if (step.revealMode == QuestRevealMode.Immediate)
        {
            RevealPendingLocally();
            return;
        }

        SetQuestUIVisible(false);
        onQuestStepChanged?.Invoke();
        Debug.Log($"퀘스트가 자막 공개를 기다리는 중: {step.questTitle}");
    }

    private void RevealPendingLocally()
    {
        currentStepIndex = pendingStepIndex;
        pendingStepIndex = -1;
        UpdateQuestUI();
        SetQuestUIVisible(true);
        onQuestStepChanged?.Invoke();
        Debug.Log($"퀘스트 공개: {CurrentStep.questTitle}");
    }

    private void CompleteAllQuests()
    {
        MarkStepsBeforeCompleted(questList.Count);
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

    public bool isClueQuestActive =>
        CurrentStep != null && !IsWaitingForSubtitleReveal && CurrentStep.questTitle.Contains("단서");

    public void AddClue()
    {
        if (CurrentStep != null)
            ReportProgress(CurrentStep.requiredSignal, $"LEGACY_CLUE_{CurrentStep.currentCount + 1}");
    }

    public bool IsQuestActive(int questIndex) =>
        !IsWaitingForSubtitleReveal && currentStepIndex == questIndex;

    public bool IsQuestActive(string questId) =>
        !IsWaitingForSubtitleReveal && CurrentStep != null &&
        string.Equals(CurrentStep.questId, questId, StringComparison.OrdinalIgnoreCase);

    private QuestStep GetStep(int index) =>
        index >= 0 && index < questList.Count ? questList[index] : null;

    private HashSet<string> GetAcceptedSources(int stepIndex)
    {
        if (!acceptedSources.TryGetValue(stepIndex, out HashSet<string> sources))
        {
            sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            acceptedSources.Add(stepIndex, sources);
        }
        return sources;
    }

    private void MarkStepsBeforeCompleted(int stepIndex)
    {
        int endExclusive = Mathf.Clamp(stepIndex, 0, questList.Count);
        for (int i = 0; i < endExclusive; i++)
        {
            QuestStep step = GetStep(i);
            if (step != null)
                step.currentCount = Mathf.Max(1, step.targetCount);
        }
    }

    private static string NormalizeSource(string sourceId, string signal) =>
        string.IsNullOrWhiteSpace(sourceId) ? signal.Trim() : sourceId.Trim();

    private static PersonController GetLocalPlayerRelay(NetworkManager manager)
    {
        NetworkObject playerObject = manager.LocalClient?.PlayerObject;
        return playerObject != null ? playerObject.GetComponent<PersonController>() : null;
    }

    private void ValidateQuestConfiguration()
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < questList.Count; i++)
        {
            QuestStep step = questList[i];
            if (step == null)
                continue;
            if (string.IsNullOrWhiteSpace(step.questId))
                Debug.LogWarning($"[QuestManager] Quest List {i}의 Quest ID가 비어 있습니다.", this);
            else if (!ids.Add(step.questId.Trim()))
                Debug.LogError($"[QuestManager] 중복 Quest ID: {step.questId}", this);
            if (!step.persistUntilSceneChange && string.IsNullOrWhiteSpace(step.requiredSignal))
                Debug.LogWarning($"[QuestManager] '{step.questTitle}'의 Required Signal이 비어 있습니다.", this);
        }
    }
}

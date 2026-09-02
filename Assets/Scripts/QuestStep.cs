using UnityEngine;

public enum QuestProgressScope
{
    SharedParty = 0,
    LocalPlayer = 1
}

public enum QuestRevealMode
{
    AfterSubtitle = 0,
    Immediate = 1
}

[System.Serializable]
public class QuestStep
{
    [Tooltip("순서가 바뀌어도 유지되는 안정적인 퀘스트 ID입니다.")]
    public string questId;
    public string questTitle;

    [Tooltip("이 단계가 허용하는 진행 신호입니다. 다른 행동은 카운트되지 않습니다.")]
    public string requiredSignal;

    [Tooltip("Shared Party는 서버가 관리하고, Local Player는 각 플레이어가 따로 진행합니다.")]
    public QuestProgressScope progressScope = QuestProgressScope.SharedParty;

    [Tooltip("이 퀘스트를 즉시 표시할지, 자막 종료 신호를 기다린 뒤 표시할지 결정합니다.")]
    public QuestRevealMode revealMode = QuestRevealMode.AfterSubtitle;

    [Min(0)] public int targetCount = 1;

    [Tooltip("같은 sourceId가 반복 보고될 때 한 번만 카운트합니다.")]
    public bool rejectDuplicateSources = true;

    [Tooltip("외부 진행 요청으로 완료하지 않고, 현재 씬이 끝날 때까지 이 단계를 유지합니다.")]
    public bool persistUntilSceneChange;

    [HideInInspector] public int currentCount;

    public bool AcceptsSignal(string signal)
    {
        return !string.IsNullOrWhiteSpace(requiredSignal) &&
               string.Equals(requiredSignal.Trim(), signal?.Trim(),
                   System.StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCompleted()
    {
        return !persistUntilSceneChange && currentCount >= Mathf.Max(1, targetCount);
    }
}

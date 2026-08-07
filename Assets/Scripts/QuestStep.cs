using UnityEngine;

// 이 한 줄이 반드시 들어가 있어야 QuestManager 인스펙터에 리스트가 노출됩니다!
[System.Serializable] 
public class QuestStep
{
    public string questTitle;       
    public int targetCount;         

    [Tooltip("외부 진행 요청으로 완료하지 않고, 현재 씬이 끝날 때까지 이 단계를 유지합니다.")]
    public bool persistUntilSceneChange;

    [HideInInspector] 
    public int currentCount = 0;    

    public bool IsCompleted()
    {
        return !persistUntilSceneChange && currentCount >= targetCount;
    }
}

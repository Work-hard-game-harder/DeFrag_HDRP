using UnityEngine;

// 이 한 줄이 반드시 들어가 있어야 QuestManager 인스펙터에 리스트가 노출됩니다!
[System.Serializable] 
public class QuestStep
{
    public string questTitle;       
    public int targetCount;         
    
    [HideInInspector] 
    public int currentCount = 0;    

    public bool IsCompleted()
    {
        return currentCount >= targetCount;
    }
}
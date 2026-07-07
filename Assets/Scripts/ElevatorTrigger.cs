using UnityEngine;

public class ElevatorTrigger : MonoBehaviour
{
    private bool isTriggered = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered || !other.CompareTag("Player")) return;
        isTriggered = true;

        if (QuestManager.Instance != null)
        {

            QuestManager.Instance.ProgressActiveQuest(1); 
        }
        Destroy(gameObject);
    }
}
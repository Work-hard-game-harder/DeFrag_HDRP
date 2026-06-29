using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInteraction : MonoBehaviour
{

    [Header("Raycast 설정")]
    public float interactionDistance = 3f;

    [Header("UI 설정")]
    public GameObject interactionHUD;
    public TextMeshProUGUI itemText;
    public Image progressCircle;

    [Header("Hint UI")]
    public GameObject hintPanel;
    public Image hintImage;

    private float holdTime = 1.5f;
    private float currentHoldTime = 0f;
    private InteractableItem targetItem;

    private int myClueCount = 0; // 플레이어 개인 단서 카운트 (멀티플레이어에서는 서버와 동기화 필요)


    // Update is called once per frame
    void Update()
    {
        CheckInteractable();

        if (targetItem != null)
        {
            HandleInteractionInput();
        }

        if (hintPanel.activeSelf && Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Mouse1))
        {
            hintPanel.SetActive(false);
        }
        
    }

    void CheckInteractable()
    {
        Ray ray = new Ray(transform.position, transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, interactionDistance))
        {
            if( hit.collider. CompareTag("Interactable"))
            {
                InteractableItem item = hit.collider.GetComponent<InteractableItem>();
                if (item != null && !item.isInteracted)
                {
                    if (targetItem != item)
                    {
                        targetItem = item;
                        itemText.text = $"[E] {targetItem.itemName} 획득";
                        interactionHUD.SetActive(true);
                        ResetTimer();
                    }

                    return;
                }
            }
            
        }

        if (targetItem != null)
        {
            targetItem = null;
            interactionHUD.SetActive(false);
            ResetTimer();
        }

    }

    void HandleInteractionInput()
    {
        if (Input.GetKey(KeyCode.E)) //GetKeyDown은 한 프레임만 체크하기에 GetKey로 변경
        {
            currentHoldTime += Time.deltaTime;
            progressCircle.fillAmount = currentHoldTime / holdTime;
            if (currentHoldTime >= holdTime)
            {
                ExecuteInteraction();
            }
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            ResetTimer();
        }
    }
            
    

    void ResetTimer()
    {
        currentHoldTime = 0f;
        if (progressCircle != null) progressCircle.fillAmount = 0f;
        
    }

    void ExecuteInteraction()
    {
        if (targetItem == null) return;

        if(targetItem.hintSprite != null)
        {
            hintImage.sprite = targetItem.hintSprite;
            hintPanel.SetActive(true);
        }

        myClueCount++;
        Debug.Log($"단서 획득! 현재 단서 수: {myClueCount} / 6");

        targetItem.isInteracted = true;

        //NetworkRoomManager.Instance.PickUPItem(targetItem.itemName);
        //모든 플레이어가 단서 카운트를 공유하려고 하려면 네트워크 매니저 cs를 만들어서 새로 싱글톤 구조로 배치할 것!

        interactionHUD.SetActive(false);
        targetItem = null;
    }
}

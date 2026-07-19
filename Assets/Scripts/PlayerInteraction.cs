using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PlayerInteraction : MonoBehaviour
{
    [Header("Raycast 설정")]
    public float maxDistance = 7f;
    public float sphereRadius = 0.3f;

    [Header("UI 설정")]
    public GameObject interactionHUD;
    public TextMeshProUGUI itemText;
    public Image progressCircle;

    [Header("Hint UI (단서용 공유 UI)")]
    public GameObject hintPanel;
    public Image hintImage;

    private IInteractable targetInteractable;
    private float currentHoldTime = 0f;
    private float holdTime = 1.5f;
    private bool interactionEnabled = true;

    void Update()
    {
        // 1. 만약 힌트 패널이 화면에 켜져 있는 상태라면?
        if (hintPanel != null && hintPanel.activeSelf)
        {
            // 2. 플레이어가 마우스 좌클릭(0)을 하는 순간!
            if (Input.GetMouseButtonDown(0))
            {
                CloseAllUI(); // 이미 만들어 두신 이 함수를 여기서 실행해 주는 겁니다!
                TogglePlayerControl(true); 
            }
            return; // 힌트창이 열려있을 땐 아래 레이저(조준) 연산을 안 하도록 막아줌
        }

        // 힌트 패널이 꺼져있을 때만 정상적으로 조준 레이저 작동
        if (!interactionEnabled)
        {
            ResetTarget();
            return;
        }

        CheckInteractable();
        HandleInteractionInput();
    }

    void CheckInteractable()
    {
        // 카메라의 정중앙 시점과 정면 방향 계산
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        // ★ [디버그 로그 1] 현재 내 눈앞으로 나가는 레이저를 씬(Scene) 창에 녹색 선으로 그립니다.
        // 게임을 켠 상태에서 Scene 창을 보면 내 눈앞에 선이 무전기까지 닿는지 볼 수 있습니다.
        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.green);

        // 우선 무엇이든 부딪히는지 확인하기 위해 레이어 마스크를 잠시 풀고 체크합니다.
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        int collisionMask = ignoreRaycastLayer >= 0 ? ~(1 << ignoreRaycastLayer) : ~0;
        if (Physics.SphereCast(ray, sphereRadius, out hit, maxDistance, collisionMask,
            QueryTriggerInteraction.Ignore))
        {
            // ★ [디버그 로그 2] 크로스헤어가 닿는 모든 물체의 이름과 레이어를 유니티 Console 창에 띄웁니다.
            Debug.Log($"[바라보는 중] 이름: {hit.collider.name} | 레이어: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

            // 그 물체의 레이어가 Interactable인지 체크
            if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Interactable"))
            {
                IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();

                if (interactable != null)
                {
                    InteractableItem item = hit.collider.GetComponentInParent<InteractableItem>();
                    if (item != null && !item.CanInteractFrom(hit, ray.direction))
                    {
                        ResetTarget();
                        return;
                    }

                    targetInteractable = interactable;

                    if (interactionHUD != null && !interactionHUD.activeSelf)
                        interactionHUD.SetActive(true);

                    if (itemText != null)
                        itemText.text = interactable.GetInteractionText();

                    return;
                }
            }
        }

        ResetTarget();
    }

    void HandleInteractionInput()
    {
        if (targetInteractable == null) return;

        if (!targetInteractable.IsHoldInteraction())
        {
            if (progressCircle != null) progressCircle.fillAmount = 0f;
            if (Input.GetKeyDown(KeyCode.E))
            {
                ExecuteInteraction();
            }
            return;
        }

        if (Input.GetKey(KeyCode.E))
        {
            currentHoldTime += Time.deltaTime;
            if (progressCircle != null) progressCircle.fillAmount = currentHoldTime / holdTime;
            
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

    void ExecuteInteraction()
    {
        if (targetInteractable != null)
        {
            targetInteractable.Interact(this);
        }
        ResetTarget();
    }

    public void ResetTarget()
    {
        targetInteractable = null;
        if (interactionHUD != null && interactionHUD.activeSelf)
            interactionHUD.SetActive(false);
        ResetTimer();
    }

    void ResetTimer()
    {
        currentHoldTime = 0f;
        if (progressCircle != null) progressCircle.fillAmount = 0f;
    }

    public void TogglePlayerControl(bool enable)
    {
        interactionEnabled = enable;
        if (!enable) ResetTarget();
    }

    public void CloseAllUI()
    {
        if (hintPanel != null) hintPanel.SetActive(false);
        if (interactionHUD != null) interactionHUD.SetActive(false);
    }
}

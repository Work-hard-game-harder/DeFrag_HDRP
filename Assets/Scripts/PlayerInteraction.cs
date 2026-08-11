using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Collections;
using EasyPeasyFirstPersonController;
using Unity.Netcode;

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

    [Header("Scene UI Auto Binding")]
    [SerializeField] private bool automaticallyBindSceneUI = true;
    [SerializeField, Min(1)] private int uiBindingRetryFrames = 120;

    [Header("Sequence Input Lock")]
    [Tooltip("시퀀스 동안 함께 비활성화할 추가 로컬 입력 컴포넌트입니다.")]
    [SerializeField] private Behaviour[] additionalSequenceInputBehaviours;

    private IInteractable targetInteractable;
    private IInteractable heldInteractable;
    private float currentHoldTime = 0f;
    private float holdTime = 1.5f;
    private bool interactionEnabled = true;
    private Camera defaultViewCamera;
    private CameraViewSwitcher cameraViewSwitcher;
    private HintSequencePresentation activeSequence;
    private readonly Dictionary<Behaviour, bool> lockedBehaviourStates = new();
    private NetworkObject playerNetworkObject;

    private void Awake()
    {
        defaultViewCamera = GetComponent<Camera>();
        cameraViewSwitcher = GetComponentInParent<CameraViewSwitcher>(true);
        playerNetworkObject = GetComponentInParent<NetworkObject>(true);
    }

    private IEnumerator Start()
    {
        if (playerNetworkObject != null && NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening)
        {
            while (!playerNetworkObject.IsSpawned)
                yield return null;

            if (!playerNetworkObject.IsOwner)
            {
                enabled = false;
                yield break;
            }
        }

        // Network gameplay players are spawned after sceneLoaded, so the inventory
        // bootstrap's scene callback may have run before a PlayerInteraction existed.
        InventorySceneBootstrap.EnsureInstalledForGameplay();

        if (!automaticallyBindSceneUI)
            yield break;

        for (int frame = 0; frame < uiBindingRetryFrames; frame++)
        {
            if (TryBindSceneUI())
                yield break;
            yield return null;
        }

        Debug.LogError(
            "[PlayerInteraction] Could not find InteractionHUD and HintPanel in the scene Canvas.",
            this);
    }

    private bool TryBindSceneUI()
    {
        if (!PlayerInteractionUIResolver.TryResolve(out var bindings))
            return false;

        interactionHUD = bindings.InteractionHUD;
        itemText = bindings.ItemText;
        progressCircle = bindings.ProgressCircle;
        hintPanel = bindings.HintPanel;
        hintImage = bindings.HintImage;

        interactionHUD.SetActive(false);
        progressCircle.fillAmount = 0f;
        Debug.Log("[PlayerInteraction] Scene UI bound to the local player.", this);
        return true;
    }

    void Update()
    {
        if (activeSequence != null)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                GameplayInputGate.ConsumeEscape(this);
                CloseSequence();
            }
            return;
        }

        // 힌트 확인 판정은 상호작용 완료 시점에 끝난다.
        // ESC는 로컬 UI를 닫고 조작을 복구하는 역할만 담당한다.
        if (hintPanel != null && hintPanel.activeSelf)
        {
            if (Input.GetMouseButtonDown(0))
            {
                CloseAllUI();
                TogglePlayerControl(true);
            }
            return;
        }

        // 힌트 패널이 꺼져있을 때만 정상적으로 조준 레이저 작동
        if (!interactionEnabled)
        {
            ResetTarget();
            return;
        }

        if (heldInteractable == null && targetInteractable != null &&
            targetInteractable.IsHoldInteraction() && Input.GetKeyDown(KeyCode.E))
            heldInteractable = targetInteractable;

        // Once a hold starts, keep the same logical target. Complex props can have
        // several child colliders and a SphereCast may otherwise alternate between
        // them, resetting the progress every frame.
        if (heldInteractable == null)
            CheckInteractable();
        HandleInteractionInput();
    }

    void CheckInteractable()
    {
        // 카메라의 정중앙 시점과 정면 방향 계산
        Camera interactionCamera = cameraViewSwitcher != null
            ? cameraViewSwitcher.ActiveCamera
            : defaultViewCamera;

        if (interactionCamera == null)
        {
            ResetTarget();
            return;
        }

        Ray ray = interactionCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
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
        if (targetInteractable == null)
        {
            if (Input.GetKeyDown(KeyCode.E))
            {
                EquipmentController equipmentController = GetComponent<EquipmentController>();
                equipmentController?.TryUseEquippedItem();
            }

            return;
        }

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
            if (heldInteractable == null)
                heldInteractable = targetInteractable;

            if (!ReferenceEquals(heldInteractable, targetInteractable))
                return;

            currentHoldTime += Time.deltaTime;
            if (progressCircle != null) progressCircle.fillAmount = currentHoldTime / holdTime;
            
            if (currentHoldTime >= holdTime)
            {
                ExecuteInteraction();
            }
        }

        if (Input.GetKeyUp(KeyCode.E))
        {
            heldInteractable = null;
            ResetTimer();
        }
    }

    void ExecuteInteraction()
    {
        IInteractable interaction = heldInteractable ?? targetInteractable;
        if (interaction != null)
        {
            interaction.Interact(this);
        }
        heldInteractable = null;
        ResetTarget();
    }

    public void ResetTarget()
    {
        targetInteractable = null;
        if (!Input.GetKey(KeyCode.E))
            heldInteractable = null;
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

    public void OpenHint(Sprite sprite)
    {
        if (hintPanel == null || hintImage == null || sprite == null)
            return;

        hintImage.sprite = sprite;
        hintPanel.SetActive(true);
        TogglePlayerControl(false);
    }

    public void OpenSequence(HintSequencePresentation presentation)
    {
        if (presentation == null)
        {
            Debug.LogWarning("[PlayerInteraction] Sequence Presentation이 연결되지 않았습니다.", this);
            return;
        }

        if (!GameplayInputGate.TryAcquire(this))
            return;

        CloseAllUI();
        TogglePlayerControl(false);
        activeSequence = presentation;

        LockBehaviour(GetComponentInParent<StarterAssets.PersonController>(true));

        foreach (Behaviour behaviour in additionalSequenceInputBehaviours)
            LockBehaviour(behaviour);

        cameraViewSwitcher?.SetInteractionLocked(true);
        presentation.Play();
    }

    public void CloseSequence()
    {
        if (activeSequence == null)
            return;

        HintSequencePresentation sequence = activeSequence;
        activeSequence = null;
        sequence.Stop();

        foreach (KeyValuePair<Behaviour, bool> entry in lockedBehaviourStates)
        {
            if (entry.Key != null)
                entry.Key.enabled = entry.Value;
        }
        lockedBehaviourStates.Clear();

        cameraViewSwitcher?.SetInteractionLocked(false);
        GameplayInputGate.Release(this);
        TogglePlayerControl(true);
    }

    private void LockBehaviour(Behaviour behaviour)
    {
        if (behaviour == null || behaviour == this || lockedBehaviourStates.ContainsKey(behaviour))
            return;

        lockedBehaviourStates.Add(behaviour, behaviour.enabled);
        behaviour.enabled = false;
    }

    private void OnDisable()
    {
        if (activeSequence != null)
            CloseSequence();
        else
            GameplayInputGate.Release(this);
    }
}

internal static class PlayerInteractionUIResolver
{
    internal readonly struct Bindings
    {
        public Bindings(GameObject hud, TextMeshProUGUI text, Image progress,
            GameObject panel, Image image)
        {
            InteractionHUD = hud;
            ItemText = text;
            ProgressCircle = progress;
            HintPanel = panel;
            HintImage = image;
        }

        public GameObject InteractionHUD { get; }
        public TextMeshProUGUI ItemText { get; }
        public Image ProgressCircle { get; }
        public GameObject HintPanel { get; }
        public Image HintImage { get; }
    }

    internal static bool TryResolve(out Bindings bindings)
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include);
        foreach (Canvas canvas in canvases)
        {
            if (canvas == null) continue;

            Transform hud = FindDescendant(canvas.transform, "InteractionHUD");
            Transform panel = FindDescendant(canvas.transform, "HintPanel");
            if (hud == null || panel == null) continue;

            TextMeshProUGUI text =
                FindDescendant(hud, "ItemText")?.GetComponent<TextMeshProUGUI>();
            Image progress =
                FindDescendant(hud, "InteractionProgress")?.GetComponent<Image>();
            Image image =
                FindDescendant(panel, "HintImage")?.GetComponent<Image>();
            if (text == null || progress == null || image == null) continue;

            bindings = new Bindings(
                hud.gameObject, text, progress, panel.gameObject, image);
            return true;
        }

        bindings = default;
        return false;
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        if (root.name == objectName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindDescendant(root.GetChild(i), objectName);
            if (result != null) return result;
        }
        return null;
    }
}

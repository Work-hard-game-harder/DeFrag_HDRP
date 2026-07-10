using UnityEngine;
using UnityEngine.InputSystem; // New Input System 필수

[RequireComponent(typeof(CharacterController))]
public class FPSController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5.0f;
    public float runSpeed = 9.0f;
    public float jumpForce = 6.0f;
    public float gravity = 20.0f;

    [Header("Mouse Settings")]
    public float mouseSensitivity = 0.08f;
    public float lookXLimit = 45.0f;

    [Header("Performance Settings")]
    //public int targetFPS = 144;

    private CharacterController characterController;
    private Camera playerCamera;
    private Vector3 moveDirection = Vector3.zero;
    private float rotationX = 0;

    //private float fpsCount = 0;
    //private float fpsTimer = 0;
    //private string fpsText = "FPS: --";

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();

        // 마우스 고정 및 숨기기
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 프레임 제한
        QualitySettings.vSyncCount = 0;
        //Application.targetFrameRate = targetFPS;
    }

    void Update()
    {
        // 1. 마우스 회전 (시선 처리)
        if (Pointer.current != null)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            rotationX += -mouseDelta.y * mouseSensitivity;
            rotationX = Mathf.Clamp(rotationX, -lookXLimit, lookXLimit);
            playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
            transform.rotation *= Quaternion.Euler(0, mouseDelta.x * mouseSensitivity, 0);
        }

        // 2. 키보드 입력 및 달리기(Left Shift) 판단
        Vector2 moveInput = Vector2.zero;
        bool isRunning = false;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) moveInput.y = 1;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) moveInput.y = -1;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) moveInput.x = -1;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) moveInput.x = 1;

            if (Keyboard.current.leftShiftKey.isPressed) isRunning = true;
        }

        // 3. 이동 속도 계산 (달리기 vs 걷기)
        float currentSpeed = isRunning ? runSpeed : walkSpeed;
        float moveDirectionY = moveDirection.y; // 기존 수직 속도(중력) 보존

        Vector3 forward = transform.TransformDirection(Vector3.forward);
        Vector3 right = transform.TransformDirection(Vector3.right);
        moveDirection = (forward * moveInput.y * currentSpeed) + (right * moveInput.x * currentSpeed);

        // 4. 땅에 닿아있을 때 점프(Space) 처리
        if (characterController.isGrounded)
        {
            moveDirection.y = -0.5f; // 땅에 안정적으로 붙어있도록 살짝 하향 적용

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                moveDirection.y = jumpForce;
            }
        }
        else
        {
            // 공중 상태면 중력 적용
            moveDirection.y = moveDirectionY - (gravity * Time.deltaTime);
        }

        // 최종 이동 적용
        characterController.Move(moveDirection * Time.deltaTime);

        // 5. FPS 카운터 계산
        /* fpsTimer += Time.unscaledDeltaTime;
         fpsCount++;
         if (fpsTimer >= 0.5f)
         {
             fpsText = "FPS: " + Mathf.RoundToInt(fpsCount / fpsTimer);
             fpsTimer = 0;
             fpsCount = 0;
         }
     }*/

        // 6. FPS 즉시 출력
        /*void OnGUI()
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.green;
            GUI.Label(new Rect(10, 10, 100, 30), fpsText, style);
        }
    }*/
    }
}
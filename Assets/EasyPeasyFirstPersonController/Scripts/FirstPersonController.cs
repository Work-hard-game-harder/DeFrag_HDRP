namespace EasyPeasyFirstPersonController
{
    using DeFrag.Player;
    using UnityEngine;

    public partial class FirstPersonController : MonoBehaviour
    {
        [Header("Settings")]
        public float walkSpeed = 3f;
        public float sprintSpeed = 5f;
        public float crouchSpeed = 1.5f;
        public float jumpSpeed = 4f;
        public float gravity = 12f;
        public float slideDuration = 0.7f;
        public float slideSpeed = 6f;
        public float mouseSensitivity = 2f;
        public float strafeTiltAmount = 2f;

        [Header("References")]
        public Transform playerCamera;
        public Transform cameraParent;
        public Transform groundCheck;
        public LayerMask groundMask;

        [HideInInspector] public CharacterController characterController;
        [HideInInspector] public IInputManager input;
        [HideInInspector] public Vector3 moveDirection;
        [HideInInspector] public bool isGrounded;

        private PlayerBaseState currentState;
        private PlayerStateFactory states;
        private float xRotation = 0f;
        private float currentTilt;
        private float tiltVelocity;
        private Animator _animator; //애니메이터용
        private float _animBlendSpeed; // 부드러운 애니메이션 전환을 위한 변수

        public PlayerBaseState CurrentState { get => currentState; set => currentState = value; }

        [Header("Visual Settings")]
        public float normalFov = 60f;
        public float sprintFov = 75f;
        public float slideFovBoost = 5f;
        public float fovChangeSpeed = 8f;
        public float bobAmount = 0.001f;
        public float bobSpeed = 10f;
        public float recoilReturnSpeed = 5f;

        [HideInInspector] public Camera cam;
        [HideInInspector] public float targetFov;
        [HideInInspector] public float currentBobIntensity;
        [HideInInspector] public float currentBobSpeed;
        [HideInInspector] public float targetTilt;
        [HideInInspector] public Animator wakieTakieAnimator;


        private float bobTimer;
        private float fovVelocity;
        private float originalCamY;
        private ISprintGate sprintGate;

        [Header("Height Settings")]
        public float standingCameraHeight = 1.75f;
        public float crouchingCameraHeight = 1f;
        public float crouchingCharacterControllerHeight = 1f;
        [HideInInspector] public float standingCharacterControllerHeight = 1.8f;
        [HideInInspector] public Vector3 standingCharacterControllerCenter = new Vector3(0, 0.9f, 0);
        [HideInInspector] public float targetCameraY;

        [Header("Ledge Settings")]
        public LayerMask ledgeLayer;
        public float ledgeDetectionDistance = 1f;
        private float landingMomentum;

        [Header("Swimming Settings")]
        public float swimSpeed = 4f;
        public float swimSprintSpeed = 6f;
        public float waterDrag = 2f;
        public LayerMask waterMask;
        [HideInInspector] public bool isInWater;

        [Header("WakieTakie Settings")]
        public GameObject wakieTakie;
        public GameObject wakieTakieSubscrition;
        public bool hasWakieTakie = false; // [HideInInspector] 임시로 제거

        [Header("Hiding Settings")]
        [HideInInspector] public bool IsHiding = false;
        [HideInInspector] public bool isCrouching = false;

        [Header("Visual Preferences")]
        public bool useFovKick = true;
        public bool useHeadBob = true;
        public bool useCameraTilt = true;
        public bool useClimbTilt = true;

        [Header("Debug")]
        public bool currentStateDebug = true;

        void OnGUI()
        {
            if (SettingManager.IsGamePaused) return;

            if (currentState != null && Application.isEditor && currentStateDebug)
                GUILayout.Label("Current State: " + currentState.GetType().Name);
        }

        void Start()
        {
            _animator = GetComponent<Animator>(); //애니메이터 파라미터를 넘겨주는 코드
            if (_animator != null)
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        private void Awake()
        {
            cam = playerCamera.GetComponent<Camera>();
            targetFov = normalFov;
            targetCameraY = standingCameraHeight;
            originalCamY = standingCameraHeight;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            characterController = GetComponent<CharacterController>();
            standingCharacterControllerHeight = characterController.height;
            standingCharacterControllerCenter = characterController.center;
            IInputManager sourceInput = GetComponent<IInputManager>();
            sprintGate = GetComponent<PlayerStamina>();
            input = sprintGate == null ? sourceInput : new SprintGatedInput(sourceInput, sprintGate);
            states = new PlayerStateFactory(this);

            currentState = states.Grounded();
            currentState.EnterState();
        }

        private void Update()
        {
            if (SettingManager.IsGamePaused)
            {
                sprintGate?.SetSprinting(false);
                moveDirection = Vector3.zero;
                _animBlendSpeed = 0f;
                if (_animator != null)
                {
                    _animator.SetFloat("Speed", 0f);
                    _animator.SetFloat("MotionSpeed", 0f);
                }
                return;
            }

            isGrounded = Physics.CheckSphere(groundCheck.position, 0.2f, groundMask, QueryTriggerInteraction.Ignore);

            if (Input.GetKeyDown(KeyCode.R) && hasWakieTakie)
            {
                if (Input.GetKeyDown(KeyCode.R) && hasWakieTakie)
                {
                    if (currentState is PlayerWakieTakieState)
                    {
                        currentState.ExitState();
                        currentState = states.Grounded();
                        currentState.EnterState();
                        CurrentState = currentState;
                    }
                    else
                    {
                        currentState.ExitState();
                        currentState = states.WakieTakie();
                        currentState.EnterState();
                        CurrentState = currentState;
                    }
                }
            }

            if (wakieTakieSubscrition != null && wakieTakieSubscrition.activeSelf)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Destroy(wakieTakieSubscrition);
                }
            }

            if (GameState.isCutscene && currentState is not PlayerSubtitleState)
            {
                currentState = states.Subtitle();
                currentState.EnterState();
            }

            currentState.UpdateState();
            ReportSprintState();
            HandleRotation();
            UpdateVisuals();

            UpdateAnimation(); //애니메이션 업데이트
        }


        private void ReportSprintState()
        {
            if (sprintGate == null)
                return;

            bool stateSupportsSprint = currentState is PlayerGroundedState
                || currentState is PlayerWakieTakieState
                || currentState is PlayerSwimmingState;
            bool isMoving = input.moveInput != Vector2.zero;
            sprintGate.SetSprinting(stateSupportsSprint && input.sprint && isMoving);
        }

        private sealed class SprintGatedInput : IInputManager
        {
            private readonly IInputManager source;
            private readonly ISprintGate gate;

            public SprintGatedInput(IInputManager source, ISprintGate gate)
            {
                this.source = source;
                this.gate = gate;
            }

            public Vector2 moveInput => source.moveInput;
            public Vector2 lookInput => source.lookInput;
            public bool jump => source.jump;
            public bool sprint => source.sprint && gate.CanSprint;
            public bool crouch => source.crouch;
            public bool slide => source.slide;
            public bool ledgeGrab => source.ledgeGrab;
            public bool wakietakie => source.wakietakie;
        }

        public void PickUpWakieTakie()
        {
            hasWakieTakie = true;
            wakieTakieSubscrition.SetActive(true);
            wakieTakieAnimator = wakieTakie.GetComponent<Animator>();
        }
        private void HandleRotation()
        {
            float mouseX = input.lookInput.x * mouseSensitivity;
            float mouseY = input.lookInput.y * mouseSensitivity;

            transform.Rotate(Vector3.up * mouseX);

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            float strafeTilt = useCameraTilt ? (-input.moveInput.x * strafeTiltAmount) : 0;
            float combinedTargetTilt = (useCameraTilt ? targetTilt : 0) + strafeTilt;

            currentTilt = Mathf.SmoothDamp(currentTilt, combinedTargetTilt, ref tiltVelocity, 0.1f);
            playerCamera.localRotation = Quaternion.Euler(xRotation, 0, currentTilt);
        }

        public void UpdateVisuals()
        {
            if (!useFovKick)
            {
                targetFov = normalFov;
            }
            cam.fieldOfView = Mathf.SmoothDamp(cam.fieldOfView, targetFov, ref fovVelocity, 1f / fovChangeSpeed);

            landingMomentum = Mathf.Lerp(landingMomentum, 0, Time.deltaTime * 10f);
            float newY = Mathf.Lerp(cameraParent.localPosition.y, targetCameraY, Time.deltaTime * 8f);

            if (useHeadBob && characterController.velocity.magnitude > 0.1f && isGrounded)
            {
                bobTimer += Time.deltaTime * currentBobSpeed;
                float bobOffset = Mathf.Sin(bobTimer) * currentBobIntensity;
                cameraParent.localPosition = new Vector3(cameraParent.localPosition.x, newY + bobOffset, cameraParent.localPosition.z);
            }
            else
            {
                bobTimer = 0;
                cameraParent.localPosition = new Vector3(cameraParent.localPosition.x, newY, cameraParent.localPosition.z);
            }
        }
        public bool HasCeiling()
        {
            float radius = characterController.radius * 0.9f;
            Vector3 origin = transform.position + Vector3.up * (characterController.height - radius);
            float checkDistance = standingCharacterControllerHeight - characterController.height + 0.1f;

            return Physics.SphereCast(origin, radius, Vector3.up, out _, checkDistance, groundMask, QueryTriggerInteraction.Ignore);
        }
        public bool CheckLedge(out Vector3 climbPosition)
        {
            climbPosition = Vector3.zero;
            RaycastHit wallHit;
            Vector3 wallOrigin = transform.position + Vector3.up * 1.5f;

            if (Physics.Raycast(wallOrigin, transform.forward, out wallHit, ledgeDetectionDistance, ledgeLayer, QueryTriggerInteraction.Ignore))
            {
                Vector3 ledgeOrigin = wallOrigin + Vector3.up * 0.6f + transform.forward * 0.2f;
                RaycastHit ledgeHit;

                if (!Physics.Raycast(ledgeOrigin, transform.forward, 0.5f, groundMask))
                {
                    if (Physics.Raycast(ledgeOrigin + transform.forward * 0.4f, Vector3.down, out ledgeHit, 1f, groundMask))
                    {
                        climbPosition = ledgeHit.point + Vector3.up * 1f;
                        return true;
                    }
                }
            }
            return false;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (((1 << other.gameObject.layer) & waterMask) != 0)
            {
                isInWater = true;
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (((1 << other.gameObject.layer) & waterMask) != 0)
            {
                isInWater = false;
            }
        }

        // FirstPersonController.cs 내부에 추가
        private void UpdateAnimation()
        {
            if (_animator != null && input != null)
            {
                // 1. 키보드 입력값(WASD)의 크기를 가져옵니다. (안 누르면 0, 누르면 1에 가까운 값)
                float inputMagnitude = input.moveInput.magnitude;

                // 2. 입력값이 있으면 걷기/달리기 속도, 없으면 0을 타겟으로 잡습니다.
                bool isMoving = inputMagnitude > 0.1f;
                float targetSpeed = isMoving ? (input.sprint ? 6f : 2f) : 0f;

                // 만약 기존 애니메이터가 대시(Sprint) 속도를 별도로 받았다면 대시 키 입력 여부도 체크해줍니다.
                // (예: input.sprint가 true면 targetSpeed를 2.0f 정도로 설정)

                // 3. 값이 0에서 1로 변할 때 뚝 끊기지 않고 부드럽게 변하도록 보간(Lerp)해줍니다.
                _animBlendSpeed = Mathf.Lerp(_animBlendSpeed, targetSpeed, Time.deltaTime * 10.0f);

                // 4. 부드럽게 가공된 값을 애니메이터에 넘겨줍니다.
                _animator.SetFloat("Speed", _animBlendSpeed);
                _animator.SetFloat("MotionSpeed", isMoving ? inputMagnitude : 1f);

                // 5. 땅에 안정적으로 닿아있다고 강제 주입해봅니다 (Grounded가 튀는 현상 방지)
                _animator.SetBool("Grounded", isGrounded);
            }
        }

        public void OnFootstep(AnimationEvent animationEvent)
        {
            // 추후 발소리 재생 기능 연결
        }
    }
}

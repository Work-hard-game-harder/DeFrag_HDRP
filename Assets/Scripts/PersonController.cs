using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using EasyPeasyFirstPersonController;
using DeFrag.Player;
#if ENABLE_INPUT_SYSTEM 
using UnityEngine.InputSystem;
#endif

/* Note: animations are called via the controller for both the character and capsule using animator null checks
 */

namespace StarterAssets
{
    [RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM 
    [RequireComponent(typeof(PlayerInput))]
#endif
    public class PersonController : NetworkBehaviour
    {
        [Header("Player")]
        [Tooltip("Move speed of the character in m/s")]
        public float MoveSpeed = 2.0f;

        [Tooltip("Sprint speed of the character in m/s")]
        public float SprintSpeed = 5.335f;

        [Tooltip("How fast the character turns to face movement direction")]
        [Range(0.0f, 0.3f)]
        public float RotationSmoothTime = 0.12f;

        [Tooltip("Acceleration and deceleration")]
        public float SpeedChangeRate = 10.0f;

        public AudioSource AudioFootsteps;
        public AudioSource LandingAudio;
        public AudioSource AudioFoley;
        public AudioClip LandingAudioClip;
        public AudioClip[] FootstepAudioClips;
        [Range(0, 1)] public float FootstepAudioVolume = 0.5f;

        [Space(10)]
        [Tooltip("The height the player can jump")]
        public float JumpHeight = 1.2f;

        [Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
        public float Gravity = -15.0f;

        [Space(10)]
        [Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
        public float JumpTimeout = 0.50f;

        [Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
        public float FallTimeout = 0.15f;

        [Header("Player Grounded")]
        [Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
        public bool Grounded = true;

        [Tooltip("Useful for rough ground")]
        public float GroundedOffset = -0.14f;

        [Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
        public float GroundedRadius = 0.28f;

        [Tooltip("What layers the character uses as ground")]
        public LayerMask GroundLayers;

        [Header("Player Crouching")]
        [SerializeField, Min(0.1f)] private float CrouchSpeed = 1.5f;
        [SerializeField, Min(0.1f)] private float CrouchingHeight = 1.0f;
        [SerializeField, Min(0f)] private float CrouchingCameraHeight = 0.9f;
        [SerializeField, Min(0.1f)] private float PostureTransitionSpeed = 10f;
        [SerializeField] private string CrouchActionName = "Crouch";

        [Header("Cinemachine")]
        [Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
        public GameObject CinemachineCameraTarget;

        [Tooltip("How far in degrees can you move the camera up")]
        public float TopClamp = 70.0f;

        [Tooltip("How far in degrees can you move the camera down")]
        public float BottomClamp = -30.0f;

        [Tooltip("Additional degress to override the camera. Useful for fine tuning camera position when locked")]
        public float CameraAngleOverride = 0.0f;

        [Tooltip("For locking the camera position on all axis")]
        public bool LockCameraPosition = false;

        // cinemachine
        private float _cinemachineTargetYaw;
        private float _cinemachineTargetPitch;

        // player
        private float _speed;
        private float _animationBlend;
        private float _rotationVelocity;
        private float _verticalVelocity;
        private float _terminalVelocity = 53.0f;

        // timeout deltatime
        private float _jumpTimeoutDelta;
        private float _fallTimeoutDelta;

        // animation IDs
        private int _animIDSpeed;
        private int _animIDGrounded;
        private int _animIDJump;
        private int _animIDFreeFall;
        private int _animIDMotionSpeed;
        private int _animIDCrouching = Animator.StringToHash("IsCrouching");

#if ENABLE_INPUT_SYSTEM 
        private PlayerInput _playerInput;
#endif
#if ENABLE_INPUT_SYSTEM
        private InputAction _crouchAction;
#endif
        private Animator _animator;
        private CharacterController _controller;
        private StarterAssetsInputs _input;
        private GameObject _mainCamera;
        private Camera _customFirstPersonCamera; // 1인칭 카메라 참조 변수(내가 직접 제어할 카메라)

        private const float _threshold = 0.01f;

        private bool _hasAnimator;
        private SoundEmitter _soundEmitter;
        private float _standingControllerHeight;
        private Vector3 _standingControllerCenter;
        private float _standingCameraHeight;
        private bool _localIsCrouching;
        private bool _localIsHiding;
        private bool _hidingRequested;
        private ISprintGate _sprintGate;

        private readonly NetworkVariable<bool> _networkIsCrouching = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> _networkIsHiding = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool IsCrouching => !IsSpawned || IsOwner
            ? _localIsCrouching
            : _networkIsCrouching.Value;
        public bool isCrouching => IsCrouching;
        public bool IsHiding => !IsSpawned || IsOwner
            ? _localIsHiding
            : _networkIsHiding.Value;
        public bool IsSubtitleLocked => GameState.isCutscene;

        private bool IsCurrentDeviceMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return _playerInput.currentControlScheme == "KeyboardMouse";
#else
				return false;
#endif
            }
        }


        private void Awake()
        {
            _soundEmitter = GetComponentInChildren<SoundEmitter>(true);
            if (_mainCamera == null)
            {
                _mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
            }
        }

        private void Start()
        {
            _cinemachineTargetYaw = CinemachineCameraTarget.transform.rotation.eulerAngles.y;

            _hasAnimator = TryGetComponent(out _animator);
            _controller = GetComponent<CharacterController>();
            _input = GetComponent<StarterAssetsInputs>();
            ResolveSprintGate();
            _standingControllerHeight = _controller.height;
            _standingControllerCenter = _controller.center;
            if (CinemachineCameraTarget != null)
                _standingCameraHeight = CinemachineCameraTarget.transform.localPosition.y;
#if ENABLE_INPUT_SYSTEM 
            _playerInput = GetComponent<PlayerInput>();
            _crouchAction = _playerInput != null && _playerInput.actions != null
                ? _playerInput.actions.FindAction(CrouchActionName, false)
                : null;
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

            AssignAnimationIDs();

            // reset our timeouts on start
            _jumpTimeoutDelta = JumpTimeout;
            _fallTimeoutDelta = FallTimeout;

            _customFirstPersonCamera = GetComponentInChildren<Camera>(); // 1인칭 카메라 컴포넌트 참조 가져오기
        }

        private void Update()
        {
            // 현재 활성화된 씬이 LobbyScene일 경우, 움직임 및 점프 로직 차단
            // if (SceneManager.GetActiveScene().name == "LobbyScene") return;

            // 내 화면에 생성된 다른 사람의 캐릭터라면 여기서 조작 로직 통과 차단
            if (IsSpawned && !IsOwner) return;

            _hasAnimator = TryGetComponent(out _animator);

            GroundedCheck();
            UpdatePosture();
            JumpAndGravity(IsSubtitleLocked);
            Move(IsSubtitleLocked);
        }

        public void RequestNetworkSceneLoad(string targetSceneName)
        {
            if (!IsSpawned || !IsOwner || string.IsNullOrWhiteSpace(targetSceneName))
            {
                return;
            }

            if (IsServer)
            {
                TryLoadNetworkScene(targetSceneName);
                return;
            }

            RequestNetworkSceneLoadServerRpc(targetSceneName);
        }

        [ServerRpc]
        private void RequestNetworkSceneLoadServerRpc(string targetSceneName)
        {
            TryLoadNetworkScene(targetSceneName);
        }

        private static void TryLoadNetworkScene(string targetSceneName)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer || manager.SceneManager == null)
            {
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
            {
                Debug.LogError($"네트워크로 전환할 씬을 불러올 수 없습니다: {targetSceneName}");
                return;
            }

            if (SceneManager.GetActiveScene().name != targetSceneName)
            {
                manager.SceneManager.LoadScene(targetSceneName, LoadSceneMode.Single);
            }
        }

        private void LateUpdate()
        {
            // 마우스 움직임에 따라 시야 회전 로직도 차단
            // if (SceneManager.GetActiveScene().name == "LobbyScene") return;
            // 다른 사람 캐릭터의 카메라는 내가 마우스를 돌려도 안 움직이게 차단
            if ((IsSpawned && !IsOwner) || IsSubtitleLocked) return;

            // 메뉴는 각 클라이언트의 로컬 UI이므로 소유 플레이어의 시야 입력만 차단한다.
            if (SettingManager.IsMenuOpen)
            {
                _input?.LookInput(Vector2.zero);
                return;
            }

            CameraRotation();
        }

        private void AssignAnimationIDs()
        {
            _animIDSpeed = Animator.StringToHash("Speed");
            _animIDGrounded = Animator.StringToHash("Grounded");
            _animIDJump = Animator.StringToHash("Jump");
            _animIDFreeFall = Animator.StringToHash("FreeFall");
            _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
            _animIDCrouching = Animator.StringToHash("IsCrouching");
        }

        private void GroundedCheck()
        {
            // set sphere position, with offset
            Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset,
        transform.position.z);
            Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers,
              QueryTriggerInteraction.Ignore);

            // update animator if using character
            if (_hasAnimator)
            {
                _animator.SetBool(_animIDGrounded, Grounded);
            }
        }

        private void CameraRotation()
        {
            // 입력값이 임계값 이상이고 카메라가 잠기지 않았을 때 실행
            if (_input.look.sqrMagnitude >= _threshold && !LockCameraPosition)
            {
                float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;

                // 1. 좌우 회전 - 마우스 좌우 입력으로 캐릭터 몸통 회전
                float rotationVelocity = _input.look.x * deltaTimeMultiplier;
                transform.Rotate(Vector3.up * rotationVelocity);

                // 2. 상하 회전 - 마우스 위아래 입력으로 고개 각도 계산
                _cinemachineTargetPitch += _input.look.y * deltaTimeMultiplier;
            }

            // 위아래 시야각 제한
            _cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

            // 3. 최종 적용 - 1인칭 카메라 오브젝트가 존재한다면 그 카메라를 직접 위아래로 회전
            if (_customFirstPersonCamera != null)
            {
                _customFirstPersonCamera.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride, 0.0f, 0.0f);
            }
            // 혹시라도 인스펙터나 자식 구조가 바뀌었을 때를 대비한 예외 처리 타겟 회전
            else if (CinemachineCameraTarget != null)
            {
                CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch + CameraAngleOverride, 0.0f, 0.0f);
            }
        }

        private void Move(bool movementLocked)
        {
            Vector2 movementInput = movementLocked ? Vector2.zero : _input.move;
            bool wantsToSprint = !movementLocked &&
                                 !_localIsCrouching &&
                                 movementInput != Vector2.zero &&
                                 _input.sprint;
            _sprintGate?.SetSprinting(wantsToSprint);
            bool canSprint = wantsToSprint && (_sprintGate?.CanSprint ?? true);

            // 입력에 따른 가속/감속 목표 속도 설정
            float targetSpeed = _localIsCrouching ? CrouchSpeed : (canSprint ? SprintSpeed : MoveSpeed);

            if (movementInput == Vector2.zero) targetSpeed = 0.0f;

            // 현재 가로 방향 속도 계산
            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // 가속 및 감속 보간
            if (currentHorizontalSpeed < targetSpeed - speedOffset ||
        currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude,
                  Time.deltaTime * SpeedChangeRate);

                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            // 애니메이션 블렌딩용 속도 계산
            _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * SpeedChangeRate);
            if (_animationBlend < 0.01f) _animationBlend = 0f;

            // 카메라 뱡향 기준이 아닌 내 몸통 기준으로 이동 방향 결정
            Vector3 targetDirection = transform.forward * movementInput.y + transform.right * movementInput.x;

            // 플레이어 최종 이동 컴포넌트 호출
            _controller.Move(targetDirection.normalized * (_speed * Time.deltaTime) +
              new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);

            // 애니메이터에 연동된 3인칭 걷기/뛰기 파라미터 업데이트
            if (_hasAnimator)
            {
                _animator.SetFloat(_animIDSpeed, _animationBlend);
                _animator.SetFloat(_animIDMotionSpeed, inputMagnitude);
            }
        }

        private void ResolveSprintGate()
        {
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is ISprintGate sprintGate)
                {
                    _sprintGate = sprintGate;
                    return;
                }
            }
        }

        private void OnDisable()
        {
            _sprintGate?.SetSprinting(false);
        }

        private void JumpAndGravity(bool jumpLocked)
        {
            if (Grounded)
            {
                // reset the fall timeout timer
                _fallTimeoutDelta = FallTimeout;

                // update animator if using character
                if (_hasAnimator)
                {
                    _animator.SetBool(_animIDJump, false);
                    _animator.SetBool(_animIDFreeFall, false);
                }

                // stop our velocity dropping infinitely when grounded
                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                // Jump
                if (!jumpLocked && !_localIsCrouching && _input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    // the square root of H * -2 * G = how much velocity needed to reach desired height
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDJump, true);
                    }
                }

                // jump timeout
                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                // reset the jump timeout timer
                _jumpTimeoutDelta = JumpTimeout;

                // fall timeout
                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }
                else
                {
                    // update animator if using character
                    if (_hasAnimator)
                    {
                        _animator.SetBool(_animIDFreeFall, true);
                    }
                }

                // if we are not grounded, do not jump
                _input.jump = false;
            }

            // apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
            if (_verticalVelocity < _terminalVelocity)
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }
        }

        private void UpdatePosture()
        {
            bool crouchPressed = ReadCrouchInput();
            bool wantsToCrouch = !IsSubtitleLocked && (crouchPressed || _hidingRequested);

            bool hiding = wantsToCrouch && _hidingRequested;
            SetLocalPosture(wantsToCrouch, hiding);

            float targetHeight = wantsToCrouch ? CrouchingHeight : _standingControllerHeight;
            Vector3 targetCenter = wantsToCrouch
                ? new Vector3(_standingControllerCenter.x, targetHeight * 0.5f, _standingControllerCenter.z)
                : _standingControllerCenter;

            _controller.height = Mathf.MoveTowards(
                _controller.height, targetHeight, PostureTransitionSpeed * Time.deltaTime);
            _controller.center = Vector3.MoveTowards(
                _controller.center, targetCenter, PostureTransitionSpeed * Time.deltaTime);

            if (CinemachineCameraTarget != null)
            {
                Transform cameraTarget = CinemachineCameraTarget.transform;
                Vector3 localPosition = cameraTarget.localPosition;
                float targetCameraY = wantsToCrouch ? CrouchingCameraHeight : _standingCameraHeight;
                localPosition.y = Mathf.MoveTowards(
                    localPosition.y, targetCameraY, PostureTransitionSpeed * Time.deltaTime);
                cameraTarget.localPosition = localPosition;
            }
        }

        private bool ReadCrouchInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (_crouchAction != null)
            {
                bool isPressed = _crouchAction.IsPressed();
                if (_input.crouch != isPressed)
                    _input.CrouchInput(isPressed);

                return isPressed;
            }
#endif
            return _input.crouch;
        }

        /// <summary>
        /// 숨기 오브젝트가 플레이어를 완전히 숨길 때 호출하는 공용 진입점입니다.
        /// 자세 표현은 소유 플레이어가 담당하고 판정 값은 서버에서 동기화합니다.
        /// </summary>
        public void SetHiding(bool hiding)
        {
            if (IsSpawned && !IsOwner)
                return;

            _hidingRequested = hiding;
            SetLocalPosture(hiding || _localIsCrouching, hiding);
        }

        private void SetLocalPosture(bool crouching, bool hiding)
        {
            hiding &= crouching;
            if (_localIsCrouching == crouching && _localIsHiding == hiding)
                return;

            _localIsCrouching = crouching;
            _localIsHiding = hiding;
            ApplyCrouchAnimation(crouching);

            if (!IsSpawned)
                return;

            if (IsServer)
                ApplyServerPosture(crouching, hiding);
            else
                SetPostureServerRpc(crouching, hiding);
        }

        [ServerRpc]
        private void SetPostureServerRpc(bool crouching, bool hiding)
        {
            ApplyServerPosture(crouching, hiding);
        }

        private void ApplyServerPosture(bool crouching, bool hiding)
        {
            _networkIsCrouching.Value = crouching;
            _networkIsHiding.Value = crouching && hiding;

            // 클라이언트 소유 캐릭터도 서버의 실제 충돌 크기는 서버 권한 상태와 맞춰야 합니다.
            if (!IsOwner && _controller != null)
            {
                _controller.height = crouching ? CrouchingHeight : _standingControllerHeight;
                _controller.center = crouching
                    ? new Vector3(
                        _standingControllerCenter.x,
                        CrouchingHeight * 0.5f,
                        _standingControllerCenter.z)
                    : _standingControllerCenter;
            }
        }

        private void ApplyCrouchAnimation(bool crouching)
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            if (_animator != null && _animator.runtimeAnimatorController != null)
                _animator.SetBool(_animIDCrouching, crouching);
        }

        private void OnNetworkCrouchingChanged(bool previousValue, bool currentValue)
        {
            if (!IsOwner)
                ApplyCrouchAnimation(currentValue);
        }

        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
        {
            if (lfAngle < -360f) lfAngle += 360f;
            if (lfAngle > 360f) lfAngle -= 360f;
            return Mathf.Clamp(lfAngle, lfMin, lfMax);
        }

        private void OnDrawGizmosSelected()
        {
            Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
            Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

            if (Grounded) Gizmos.color = transparentGreen;
            else Gizmos.color = transparentRed;

            // when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
            Gizmos.DrawSphere(
        new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z),
        GroundedRadius);
        }

        private void OnFootstep(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {

                if (AudioFootsteps != null)
                    AudioFootsteps.Play();
                if (AudioFoley != null)
                    AudioFoley.Play();
            }
        }

        private void OnLand(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                if (LandingAudio != null)
                    LandingAudio.Play();

            }
        }

        /// <summary>
        /// 소유 플레이어의 로컬 마이크 레벨을 서버에 전달합니다.
        /// 서버만 이 값을 몬스터 감지용 SoundEmitter에 적용합니다.
        /// </summary>
        public void SubmitLocalVoiceLevel(bool isActive, float normalizedVolume)
        {
            if (!IsSpawned || !IsOwner)
                return;

            float safeVolume = Mathf.Clamp01(normalizedVolume);
            if (IsServer)
            {
                // 호스트의 SoundEmitter는 같은 프로세스에서 로컬 입력을 이미 가지고 있으므로
                // 별도의 복제 값을 덮어쓸 필요가 없습니다.
                return;
            }

            SubmitVoiceLevelServerRpc(isActive, safeVolume);
        }

        [ServerRpc]
        private void SubmitVoiceLevelServerRpc(bool isActive, float normalizedVolume)
        {
            if (_soundEmitter == null)
                _soundEmitter = GetComponentInChildren<SoundEmitter>(true);

            _soundEmitter?.ApplyNetworkVoiceLevel(isActive, Mathf.Clamp01(normalizedVolume));
        }

        public override void OnNetworkSpawn()
        {
            _networkIsCrouching.OnValueChanged += OnNetworkCrouchingChanged;

            if (_soundEmitter == null)
                _soundEmitter = GetComponentInChildren<SoundEmitter>(true);

            ConfigureLocalPlayerPresentation();
            ApplyCrouchAnimation(IsCrouching);

            if (IsOwner)
            {
                TvMonsterProximityGlitch glitch =
                    GetComponentInChildren<TvMonsterProximityGlitch>(true);
                glitch?.InitializeForConfirmedLocalOwner();
                RequestLobbyBroadcastStateServerRpc();
            }

            // 내가 이 캐릭터의 주인(로컬 플레이어)일 때만 위치 변경 작업 수행
            if (IsOwner)
            {
                // SpawnPoint 태그 가진 오브젝트들 찾기
                GameObject[] spawnPoints = GameObject.FindGameObjectsWithTag("SpawnPoint");

                if (spawnPoints.Length > 0)
                {
                    // 내 고유 ClientId 번호에 맞춰 겹치지 않게 스폰포인트 배정
                    int index = (int)NetworkManager.Singleton.LocalClientId % spawnPoints.Length;
                    Transform targetPoint = spawnPoints[index].transform;

                    // CharacterController 컴포넌트가 켜져 있으면 위치 변경이 되지 않을 수 있음
                    // 따라서 CharacterController 컴포넌트를 잠시 비활성화
                    CharacterController cc = GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    // 지정 스폰 포인트로 스폰
                    transform.position = targetPoint.position;
                    transform.rotation = targetPoint.rotation;

                    // 이동이 끝났으므로 다시 CharacterController 활성화
                    if (cc != null) cc.enabled = true;
                }
                else
                {
                    Debug.LogWarning("씬에 'SpawnPoint' 태그를 가진 오브젝트가 하나도 없습니다! (0,0,0)에 스폰됩니다.");
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _networkIsCrouching.OnValueChanged -= OnNetworkCrouchingChanged;
            SetLocalInputEnabled(false);
            base.OnNetworkDespawn();
        }

        public void RequestSharedQuestProgress(string signal, string sourceId, int amount)
        {
            if (string.IsNullOrWhiteSpace(signal) || amount <= 0)
                return;

            if (IsSpawned && IsOwner)
                ReportSharedQuestProgressServerRpc(signal, sourceId ?? string.Empty, amount);
        }

        [ServerRpc]
        private void ReportSharedQuestProgressServerRpc(
            string signal,
            string sourceId,
            int amount)
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null ||
                !manager.TryReportSharedProgressOnServer(signal, sourceId, amount))
                return;

            manager.BroadcastSharedSnapshotFromServer();
        }

        public void RequestSharedQuestReveal()
        {
            if (IsSpawned && IsOwner)
                RevealSharedQuestServerRpc();
        }

        [ServerRpc]
        private void RevealSharedQuestServerRpc()
        {
            QuestManager manager = QuestManager.Instance;
            if (manager == null || !manager.TryRevealSharedPendingOnServer())
                return;

            manager.BroadcastSharedSnapshotFromServer();
        }

        public void BroadcastSharedQuestSnapshotFromServer(
            int currentStepIndex,
            int pendingStepIndex,
            int currentCount)
        {
            if (IsSpawned && IsServer)
                ApplySharedQuestSnapshotClientRpc(
                    currentStepIndex,
                    pendingStepIndex,
                    currentCount);
        }

        [ClientRpc]
        private void ApplySharedQuestSnapshotClientRpc(
            int currentStepIndex,
            int pendingStepIndex,
            int currentCount)
        {
            if (IsServer)
                return;

            QuestManager.Instance?.ApplySharedSnapshot(
                currentStepIndex,
                pendingStepIndex,
                currentCount);
        }

        public void RequestLobbyHintConfirmation(string hintId)
        {
            if (string.IsNullOrWhiteSpace(hintId)) return;

            if (IsSpawned && IsOwner)
                ConfirmLobbyHintServerRpc(hintId);
        }

        [ServerRpc]
        private void ConfirmLobbyHintServerRpc(string hintId)
        {
            HintConfirmationTracker tracker = HintConfirmationTracker.Instance;
            if (tracker == null ||
                !tracker.TryConfirmOnServer(hintId, out int count, out bool emergency))
                return;

            ApplyLobbyHintConfirmationClientRpc(hintId, count, emergency);
        }

        [ClientRpc]
        private void ApplyLobbyHintConfirmationClientRpc(
            string hintId,
            int count,
            bool emergency)
        {
            HintConfirmationTracker.Instance?.ApplyServerConfirmation(
                hintId, count, emergency, this);
        }

        public void RequestLobbyBroadcastStart(string broadcastId, float duration)
        {
            if (string.IsNullOrWhiteSpace(broadcastId) || duration <= 0f) return;

            if (IsSpawned && IsOwner)
                StartLobbyBroadcastServerRpc(broadcastId, duration);
        }

        [ServerRpc]
        private void StartLobbyBroadcastServerRpc(string broadcastId, float duration)
        {
            HintConfirmationTracker tracker = HintConfirmationTracker.Instance;
            float safeDuration = Mathf.Clamp(duration, 1f, 300f);
            if (tracker == null ||
                !tracker.TryStartBroadcastOnServer(
                    broadcastId,
                    safeDuration,
                    out double startTime))
                return;

            ApplyLobbyBroadcastClientRpc(broadcastId, startTime, safeDuration);
        }

        [ServerRpc]
        private void RequestLobbyBroadcastStateServerRpc(ServerRpcParams rpcParams = default)
        {
            HintConfirmationTracker tracker = HintConfirmationTracker.Instance;
            if (tracker == null ||
                !tracker.TryGetActiveBroadcastOnServer(
                    out string broadcastId,
                    out double startTime,
                    out float duration))
                return;

            ClientRpcParams target = new()
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { rpcParams.Receive.SenderClientId }
                }
            };
            ApplyLobbyBroadcastClientRpc(
                broadcastId, startTime, duration, target);
        }

        [ClientRpc]
        private void ApplyLobbyBroadcastClientRpc(
            string broadcastId,
            double startTime,
            float duration,
            ClientRpcParams rpcParams = default)
        {
            HintConfirmationTracker.Instance?.ApplyServerBroadcastStart(
                broadcastId, startTime, duration, this);
        }

        private void ConfigureLocalPlayerPresentation()
        {
            SetLocalInputEnabled(IsOwner);

            CameraViewSwitcher cameraViewSwitcher =
                GetComponentInChildren<CameraViewSwitcher>(true);
            if (cameraViewSwitcher != null)
            {
                cameraViewSwitcher.SetLocalPresentationEnabled(IsOwner);
                return;
            }

            foreach (Camera playerCamera in GetComponentsInChildren<Camera>(true))
            {
                playerCamera.enabled = IsOwner;
            }

            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
            {
                listener.enabled = IsOwner;
            }
        }

        private void SetLocalInputEnabled(bool isEnabled)
        {
#if ENABLE_INPUT_SYSTEM
            PlayerInput playerInput = GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = isEnabled;
            }
#endif

            StarterAssetsInputs inputs = GetComponent<StarterAssetsInputs>();
            if (inputs == null)
            {
                return;
            }

            if (!isEnabled)
            {
                inputs.MoveInput(Vector2.zero);
                inputs.LookInput(Vector2.zero);
                inputs.JumpInput(false);
                inputs.SprintInput(false);
            }
        }
    }
}

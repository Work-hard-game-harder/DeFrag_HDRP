using EasyPeasyFirstPersonController;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.Player
{
    /// <summary>
    /// 소유 플레이어의 SoundEmitter 음성 레벨을 말하기 상태로 변환하고,
    /// 서버 권한 NetworkVariable로 동기화해 다른 플레이어 머리 위에 표시합니다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SoundEmitter))]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkVoiceActivityIndicator : NetworkBehaviour
    {
        [Header("Voice Detection")]
        [SerializeField, Range(0f, 1f)] private float speakingStartThreshold = 0.05f;
        [SerializeField, Range(0f, 1f)] private float speakingStopThreshold = 0.025f;
        [SerializeField, Min(0f)] private float releaseDelay = 0.2f;
        [SerializeField] private bool hideWhileWalkieTalkieTransmitting = true;

        [Header("World Indicator")]
        [SerializeField] private Sprite microphoneSprite;
        [SerializeField] private Color indicatorColor = Color.white;
        [SerializeField, Min(0.01f)] private float indicatorWorldSize = 0.32f;
        [Tooltip("Head 본 기준 위치입니다. X는 좌우, Y는 높이, Z는 앞뒤 방향입니다.")]
        [SerializeField] private Vector3 indicatorOffsetFromHead = new Vector3(0f, 0.35f, 0f);
        [Tooltip("Humanoid Head 본을 찾지 못했을 때만 사용하는 루트 기준 위치입니다.")]
        [SerializeField] private Vector3 fallbackRootOffset = new Vector3(0f, 2.2f, 0f);
        [SerializeField] private bool showForOwner;

        private readonly NetworkVariable<bool> networkIsSpeaking = new(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private SoundEmitter soundEmitter;
        private Transform headAnchor;
        private GameObject indicatorRoot;
        private Camera viewerCamera;
        private bool localIsSpeaking;
        private float belowThresholdSince = -1f;

        public bool IsSpeaking => IsSpawned ? networkIsSpeaking.Value : localIsSpeaking;

        private void Awake()
        {
            soundEmitter = GetComponent<SoundEmitter>();
            ResolveHeadAnchor();
            CreateIndicator();
            ApplyIndicator(false);
        }

        public override void OnNetworkSpawn()
        {
            networkIsSpeaking.OnValueChanged += OnSpeakingChanged;
            ApplyIndicator(networkIsSpeaking.Value);
        }

        public override void OnNetworkDespawn()
        {
            networkIsSpeaking.OnValueChanged -= OnSpeakingChanged;
            ApplyIndicator(false);
        }

        private void Update()
        {
            if (IsSpawned)
            {
                if (!IsOwner)
                    return;

                UpdateOwnedVoiceState();
                return;
            }

            // NetworkManager가 없는 단독 테스트 씬에서도 로컬 표시 상태를 검증할 수 있습니다.
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
            {
                UpdateOwnedVoiceState();
                ApplyIndicator(localIsSpeaking);
            }
        }

        private void LateUpdate()
        {
            if (indicatorRoot == null || !indicatorRoot.activeSelf)
                return;

            if (headAnchor == null)
                ResolveHeadAnchor();

            indicatorRoot.transform.position = headAnchor != null
                ? GetHeadIndicatorPosition()
                : transform.TransformPoint(fallbackRootOffset);

            if (viewerCamera == null || !viewerCamera.isActiveAndEnabled)
                viewerCamera = Camera.main;

            if (viewerCamera != null)
                indicatorRoot.transform.rotation = viewerCamera.transform.rotation;
        }

        private void UpdateOwnedVoiceState()
        {
            bool nextState = EvaluateSpeakingState();
            if (nextState == localIsSpeaking)
                return;

            localIsSpeaking = nextState;

            if (!IsSpawned)
                return;

            if (IsServer)
                networkIsSpeaking.Value = nextState;
            else
                SetSpeakingServerRpc(nextState);
        }

        private bool EvaluateSpeakingState()
        {
            if (soundEmitter == null)
                return false;

            if (hideWhileWalkieTalkieTransmitting && soundEmitter.IsWalkieTransmitting)
            {
                belowThresholdSince = -1f;
                return false;
            }

            float volume = soundEmitter.IsMicActive ? soundEmitter.CurrentVolume : 0f;
            if (!localIsSpeaking)
            {
                belowThresholdSince = -1f;
                return volume >= speakingStartThreshold;
            }

            if (volume >= speakingStopThreshold)
            {
                belowThresholdSince = -1f;
                return true;
            }

            if (belowThresholdSince < 0f)
                belowThresholdSince = Time.unscaledTime;

            return Time.unscaledTime - belowThresholdSince < releaseDelay;
        }

        [ServerRpc]
        private void SetSpeakingServerRpc(bool speaking)
        {
            networkIsSpeaking.Value = speaking;
        }

        private void OnSpeakingChanged(bool previousValue, bool currentValue)
        {
            ApplyIndicator(currentValue);
        }

        private void ApplyIndicator(bool speaking)
        {
            if (indicatorRoot == null)
                return;

            bool canShowForThisClient = !IsSpawned || showForOwner || !IsOwner;
            indicatorRoot.SetActive(speaking && canShowForThisClient);
        }

        private void ResolveHeadAnchor()
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman)
                headAnchor = animator.GetBoneTransform(HumanBodyBones.Head);
        }

        private Vector3 GetHeadIndicatorPosition()
        {
            // Head 회전 애니메이션 때문에 아이콘이 흔들리지 않도록 플레이어 루트의
            // 좌우/앞뒤 방향과 월드 Y축을 조합해 위치 오프셋만 적용합니다.
            return headAnchor.position
                + transform.right * indicatorOffsetFromHead.x
                + Vector3.up * indicatorOffsetFromHead.y
                + transform.forward * indicatorOffsetFromHead.z;
        }

        private void CreateIndicator()
        {
            if (indicatorRoot != null)
                return;

            indicatorRoot = new GameObject(
                "Voice Activity Indicator",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasRenderer),
                typeof(Image));
            indicatorRoot.layer = gameObject.layer;
            indicatorRoot.transform.SetParent(transform, false);

            RectTransform rectTransform = indicatorRoot.GetComponent<RectTransform>();
            rectTransform.sizeDelta = Vector2.one * indicatorWorldSize;

            Canvas canvas = indicatorRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;

            Image image = indicatorRoot.GetComponent<Image>();
            image.sprite = microphoneSprite;
            image.color = indicatorColor;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        private void OnValidate()
        {
            speakingStartThreshold = Mathf.Clamp01(speakingStartThreshold);
            speakingStopThreshold = Mathf.Clamp(speakingStopThreshold, 0f, speakingStartThreshold);
            releaseDelay = Mathf.Max(0f, releaseDelay);
            indicatorWorldSize = Mathf.Max(0.01f, indicatorWorldSize);
        }
    }
}

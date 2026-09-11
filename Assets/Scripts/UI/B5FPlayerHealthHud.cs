using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.UI
{
    /// <summary>
    /// B5F 씬에서 로컬 플레이어의 체력만 표시하는 씬 전용 HUD입니다.
    /// 체력의 권한과 변경은 PlayerStats/NetworkPlayerHealth가 계속 담당합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class B5FPlayerHealthHud : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private Vector2 panelSize = new Vector2(260f, 64f);
        [SerializeField] private Vector2 screenOffset = new Vector2(28f, -28f);
        [SerializeField] private int fontSize = 28;

        [Header("Colors")]
        [SerializeField] private Color panelColor = new Color(0f, 0f, 0f, 0.7f);
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color criticalColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField, Range(0f, 1f)] private float criticalRatio = 0.3f;

        private Canvas canvas;
        private TextMeshProUGUI healthText;
        private PlayerStats observedStats;
        private Coroutine bindingRoutine;

        private void Awake()
        {
            CreateHud();
        }

        private void OnEnable()
        {
            bindingRoutine = StartCoroutine(BindLocalPlayerWhenReady());
        }

        private void OnDisable()
        {
            if (bindingRoutine != null)
            {
                StopCoroutine(bindingRoutine);
                bindingRoutine = null;
            }

            Unbind();
        }

        private IEnumerator BindLocalPlayerWhenReady()
        {
            canvas.enabled = false;

            while (isActiveAndEnabled)
            {
                PlayerStats localStats = ResolveLocalPlayerStats();
                if (localStats != null)
                {
                    Bind(localStats);
                    yield break;
                }

                yield return null;
            }
        }

        private PlayerStats ResolveLocalPlayerStats()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                NetworkObject localPlayer = manager.LocalClient?.PlayerObject;
                return localPlayer != null ? localPlayer.GetComponent<PlayerStats>() : null;
            }

            // B5F를 단독 실행할 때만 씬 내 PlayerStats를 테스트 대상으로 사용합니다.
            return FindFirstObjectByType<PlayerStats>();
        }

        private void Bind(PlayerStats playerStats)
        {
            Unbind();
            observedStats = playerStats;
            observedStats.HealthChanged += HandleHealthChanged;
            canvas.enabled = true;
            Refresh(observedStats.Health, observedStats.MaxHealth);
        }

        private void Unbind()
        {
            if (observedStats != null)
                observedStats.HealthChanged -= HandleHealthChanged;

            observedStats = null;
        }

        private void HandleHealthChanged(int previousHealth, int currentHealth)
        {
            if (observedStats != null)
                Refresh(currentHealth, observedStats.MaxHealth);
        }

        private void Refresh(int currentHealth, int maxHealth)
        {
            int safeMaxHealth = Mathf.Max(1, maxHealth);
            healthText.text = $"HP  {currentHealth} / {safeMaxHealth}";
            healthText.color = currentHealth / (float)safeMaxHealth <= criticalRatio
                ? criticalColor
                : normalColor;
        }

        private void CreateHud()
        {
            GameObject canvasObject = new GameObject(
                "B5F Health Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject(
                "Health Panel",
                typeof(RectTransform),
                typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);

            RectTransform panelTransform = panelObject.GetComponent<RectTransform>();
            panelTransform.anchorMin = new Vector2(0f, 1f);
            panelTransform.anchorMax = new Vector2(0f, 1f);
            panelTransform.pivot = new Vector2(0f, 1f);
            panelTransform.anchoredPosition = screenOffset;
            panelTransform.sizeDelta = panelSize;
            panelObject.GetComponent<Image>().color = panelColor;

            GameObject textObject = new GameObject(
                "Health Text",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(panelObject.transform, false);

            RectTransform textTransform = textObject.GetComponent<RectTransform>();
            textTransform.anchorMin = Vector2.zero;
            textTransform.anchorMax = Vector2.one;
            textTransform.offsetMin = new Vector2(18f, 6f);
            textTransform.offsetMax = new Vector2(-18f, -6f);

            healthText = textObject.GetComponent<TextMeshProUGUI>();
            healthText.fontSize = fontSize;
            healthText.fontStyle = FontStyles.Bold;
            healthText.alignment = TextAlignmentOptions.MidlineLeft;
            healthText.raycastTarget = false;
        }
    }
}

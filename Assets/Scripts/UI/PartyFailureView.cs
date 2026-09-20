using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DeFrag.UI
{
    /// <summary>
    /// DeathScreen 프리팹의 표시만 담당합니다. 레이아웃과 색상은 프리팹에서 편집합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PartyFailureView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text deathMessage;
        [SerializeField, TextArea] private string teammateFailureMessage = "전원 생존 실패\n\nMISSION FAILED";
        [SerializeField] private GameObject hostButtons;
        [SerializeField] private Button returnToLobbyButton;
        [SerializeField] private Button stageSelectionButton;

        public void Initialize(
            bool showHostButtons,
            bool isDeceasedLocalPlayer,
            float fadeDuration,
            UnityAction returnToLobby,
            UnityAction openStageSelection)
        {
            // 실제 사망자는 프리팹에 작성된 기본 문구를 그대로 사용합니다.
            if (!isDeceasedLocalPlayer && deathMessage != null)
                deathMessage.text = teammateFailureMessage;

            if (hostButtons != null)
                hostButtons.SetActive(showHostButtons);

            if (returnToLobbyButton != null)
            {
                returnToLobbyButton.onClick.RemoveAllListeners();
                returnToLobbyButton.onClick.AddListener(returnToLobby);
            }

            if (stageSelectionButton != null)
            {
                stageSelectionButton.onClick.RemoveAllListeners();
                stageSelectionButton.onClick.AddListener(openStageSelection);
            }

            StartCoroutine(FadeIn(fadeDuration));
        }

        private IEnumerator FadeIn(float duration)
        {
            if (canvasGroup == null)
                yield break;

            canvasGroup.alpha = 0f;
            float safeDuration = Mathf.Max(0.01f, duration);
            float elapsed = 0f;
            while (elapsed < safeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Clamp01(elapsed / safeDuration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }
    }

    /// <summary>네트워크 연결 종료 안내만 담당하는 로컬 UI입니다.</summary>
    [DisallowMultipleComponent]
    public sealed class DisconnectNotificationPresenter : MonoBehaviour
    {
        private const string ObjectName = "Disconnect Notification";
        private const int SortingOrder = 5000;

        private string targetSceneName;
        private string message;
        private Sprite windowSprite;
        private TMP_FontAsset fontAsset;
        private float visibleDuration;
        private float fadeDuration;

        public static void ShowNow(
            string message,
            Sprite windowSprite,
            TMP_FontAsset fontAsset,
            float visibleDuration,
            float fadeDuration)
        {
            DisconnectNotificationPresenter presenter = Create(
                message, windowSprite, fontAsset, visibleDuration, fadeDuration);
            presenter.BuildAndShow();
        }

        public static void ShowAfterSceneLoad(
            string targetSceneName,
            string message,
            Sprite windowSprite,
            TMP_FontAsset fontAsset,
            float visibleDuration,
            float fadeDuration)
        {
            DisconnectNotificationPresenter presenter = Create(
                message, windowSprite, fontAsset, visibleDuration, fadeDuration);
            presenter.targetSceneName = targetSceneName;
            DontDestroyOnLoad(presenter.gameObject);
            SceneManager.sceneLoaded += presenter.HandleSceneLoaded;
        }

        private static DisconnectNotificationPresenter Create(
            string message,
            Sprite windowSprite,
            TMP_FontAsset fontAsset,
            float visibleDuration,
            float fadeDuration)
        {
            DisconnectNotificationPresenter existing =
                FindAnyObjectByType<DisconnectNotificationPresenter>();
            if (existing != null)
                Destroy(existing.gameObject);

            GameObject root = new GameObject(ObjectName);
            DisconnectNotificationPresenter presenter =
                root.AddComponent<DisconnectNotificationPresenter>();
            presenter.message = message;
            presenter.windowSprite = windowSprite;
            presenter.fontAsset = fontAsset;
            presenter.visibleDuration = Mathf.Max(0.1f, visibleDuration);
            presenter.fadeDuration = Mathf.Max(0.01f, fadeDuration);
            return presenter;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != targetSceneName)
                return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            BuildAndShow();
        }

        private void BuildAndShow()
        {
            ResolveFontFromLoadedScene();

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            CanvasGroup canvasGroup = gameObject.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            GameObject window = new GameObject(
                "Window", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            window.transform.SetParent(transform, false);
            RectTransform windowRect = (RectTransform)window.transform;
            windowRect.anchorMin = new Vector2(0.5f, 0.5f);
            windowRect.anchorMax = new Vector2(0.5f, 0.5f);
            windowRect.sizeDelta = new Vector2(820f, 260f);

            Image windowImage = window.GetComponent<Image>();
            windowImage.sprite = windowSprite;
            windowImage.type = windowSprite != null ? Image.Type.Sliced : Image.Type.Simple;
            windowImage.color = windowSprite != null
                ? Color.white
                : new Color(0.02f, 0.08f, 0.12f, 0.95f);
            windowImage.raycastTarget = false;

            GameObject label = new GameObject(
                "Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            label.transform.SetParent(window.transform, false);
            RectTransform labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(70f, 45f);
            labelRect.offsetMax = new Vector2(-70f, -45f);

            TextMeshProUGUI text = label.GetComponent<TextMeshProUGUI>();
            text.text = message;
            if (fontAsset != null)
                text.font = fontAsset;
            text.fontSize = 36f;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;

            StartCoroutine(ShowRoutine(canvasGroup));
        }

        private void ResolveFontFromLoadedScene()
        {
            if (fontAsset != null)
                return;

            TextMeshProUGUI[] sceneTexts =
                FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include);
            foreach (TextMeshProUGUI sceneText in sceneTexts)
            {
                TMP_FontAsset candidate = sceneText != null ? sceneText.font : null;
                if (candidate != null && candidate.HasCharacters(message))
                {
                    fontAsset = candidate;
                    return;
                }
            }

            Debug.LogError(
                "[DisconnectNotification] 한글을 지원하는 TMP 폰트를 찾지 못했습니다.",
                this);
        }

        private IEnumerator ShowRoutine(CanvasGroup canvasGroup)
        {
            yield return Fade(canvasGroup, 0f, 1f);
            yield return new WaitForSecondsRealtime(visibleDuration);
            yield return Fade(canvasGroup, 1f, 0f);
            Destroy(gameObject);
        }

        private IEnumerator Fade(CanvasGroup canvasGroup, float from, float to)
        {
            canvasGroup.alpha = from;
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration));
                yield return null;
            }

            canvasGroup.alpha = to;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }
    }
}

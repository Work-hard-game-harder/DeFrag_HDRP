using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 씬 진입 시 기존 SubtitleTrigger를 재생하고, 해당 로컬 화면에만
/// 검정 배경과 선택적 Sprite 시퀀스를 표시합니다.
/// 네트워크 게임 상태를 만들거나 동기화하지 않는 순수 프레젠테이션 컴포넌트입니다.
/// </summary>
public sealed class SubtitleIntroPresentation : MonoBehaviour
{
    [Header("Sequence")]
    [SerializeField] private SubtitleTrigger subtitleTrigger;
    [Min(0f)] [SerializeField] private float startDelay;

    [Header("Black Background")]
    [SerializeField] private Color backgroundColor = Color.black;
    [SerializeField] private int backgroundSortingOrder = 31999;
    [Min(0f)] [SerializeField] private float fadeOutDuration = 0.25f;

    private CanvasGroup backgroundGroup;
    private Canvas introCanvas;
    private Coroutine startRoutine;
    private Coroutine finishRoutine;
    private bool hasStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestartScenePresentations()
    {
        // Enter Play Mode Options에서 Domain/Scene Reload를 모두 꺼도 이 메서드는
        // 플레이 진입마다 호출됩니다. 이전 실행의 인스턴스 상태를 명시적으로 초기화합니다.
        SubtitleIntroPresentation[] presentations =
            FindObjectsByType<SubtitleIntroPresentation>(FindObjectsInactive.Include);
        foreach (SubtitleIntroPresentation presentation in presentations)
            presentation.BeginForCurrentPlaySession();
    }

    private void Awake()
    {
        CreateBackground();
    }

    private void OnEnable()
    {
        if (Application.isPlaying)
            BeginForCurrentPlaySession();
    }

    private void BeginForCurrentPlaySession()
    {
        if (!isActiveAndEnabled)
            return;

        if (startRoutine != null)
            StopCoroutine(startRoutine);
        if (finishRoutine != null)
        {
            StopCoroutine(finishRoutine);
            finishRoutine = null;
        }

        hasStarted = false;
        HideImmediately();
        CreateBackground();
        startRoutine = StartCoroutine(PlayAfterInitialization());
    }

    private IEnumerator PlayAfterInitialization()
    {
        // SubtitlesScript.Start가 초기 UI를 정리한 다음 재생합니다.
        yield return null;

        // LobbyF 오프닝 시네마틱이 재생 중이면 끝난 뒤에 시작합니다.
        while (CinematicPlayback.IsCoveringGameplay)
            yield return null;

        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);

        startRoutine = null;
        PlayIntro();
    }

    public void PlayIntro()
    {
        if (hasStarted)
            return;

        hasStarted = true;

        if (subtitleTrigger == null)
        {
            Debug.LogWarning(
                $"[{nameof(SubtitleIntroPresentation)}] Subtitle Trigger가 연결되지 않았습니다.",
                this);
            HideImmediately();
            return;
        }

        ShowBackground();

        // Trigger0은 씬 시작 전용이므로 시작 재생과 함께 one-shot 상태를
        // 소비해 이후 물리 접촉으로 같은 자막이 중복 재생되지 않게 합니다.
        subtitleTrigger.PlaySubtitleFromInteract(FinishIntro);
    }

    private void FinishIntro()
    {
        if (!isActiveAndEnabled || fadeOutDuration <= 0f)
        {
            HideImmediately();
            return;
        }

        if (finishRoutine != null)
            StopCoroutine(finishRoutine);

        finishRoutine = StartCoroutine(FadeOutBackground());
    }

    private IEnumerator FadeOutBackground()
    {
        if (backgroundGroup == null)
            yield break;

        float elapsed = 0f;
        float startAlpha = backgroundGroup.alpha;

        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            backgroundGroup.alpha = Mathf.Lerp(
                startAlpha,
                0f,
                Mathf.Clamp01(elapsed / fadeOutDuration));
            yield return null;
        }

        backgroundGroup.gameObject.SetActive(false);
        finishRoutine = null;
    }

    private void CreateBackground()
    {
        if (introCanvas != null)
            return;

        if (subtitleTrigger == null || subtitleTrigger.subtitlesScript == null)
            return;

        GameObject subtitlePanel = subtitleTrigger.subtitlesScript.subtitlesPanel;
        if (subtitlePanel == null)
        {
            Debug.LogWarning(
                $"[{nameof(SubtitleIntroPresentation)}] Subtitle Panel이 연결되지 않았습니다.",
                this);
            return;
        }

        Canvas sourceCanvas = subtitlePanel.GetComponentInParent<Canvas>();
        if (sourceCanvas == null)
        {
            Debug.LogWarning(
                $"[{nameof(SubtitleIntroPresentation)}] Subtitle Canvas를 찾을 수 없습니다.",
                this);
            return;
        }

        // 배경과 자막을 서로 다른 중첩 Canvas에 두면 최초 활성화 시점의
        // Canvas 갱신 순서에 따라 검정 배경만 남을 수 있습니다.
        // 둘을 하나의 최상위 Overlay Canvas에 넣고 형제 순서로 앞뒤를 고정합니다.
        GameObject overlay = new GameObject(
            "Subtitle Intro Overlay",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        overlay.layer = subtitlePanel.layer;

        introCanvas = overlay.GetComponent<Canvas>();
        introCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        introCanvas.overrideSorting = true;
        introCanvas.sortingLayerID = GetHighestSortingLayerId();
        introCanvas.sortingOrder = backgroundSortingOrder + 1;

        CopyCanvasScaler(sourceCanvas, overlay.GetComponent<CanvasScaler>());

        GameObject background = new GameObject(
            "Black Background",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(CanvasGroup));
        background.layer = subtitlePanel.layer;

        RectTransform rect = background.GetComponent<RectTransform>();
        rect.SetParent(overlay.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.SetAsFirstSibling();

        Image image = background.GetComponent<Image>();
        image.color = backgroundColor;
        image.raycastTarget = true;

        backgroundGroup = background.GetComponent<CanvasGroup>();
        backgroundGroup.alpha = 1f;
        backgroundGroup.interactable = true;
        backgroundGroup.blocksRaycasts = true;

        // 기존 자막 UI의 RectTransform 값은 유지하고 Canvas만 안정적인 최상위
        // Overlay 아래로 옮깁니다. Assist는 자막 Panel의 자식이므로 함께 이동합니다.
        subtitlePanel.transform.SetParent(overlay.transform, false);
        subtitlePanel.transform.SetAsLastSibling();
    }

    private static void CopyCanvasScaler(Canvas sourceCanvas, CanvasScaler targetScaler)
    {
        CanvasScaler sourceScaler = sourceCanvas.GetComponent<CanvasScaler>();
        if (sourceScaler == null)
        {
            targetScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            targetScaler.referenceResolution = new Vector2(1920f, 1080f);
            targetScaler.matchWidthOrHeight = 0.5f;
            return;
        }

        targetScaler.uiScaleMode = sourceScaler.uiScaleMode;
        targetScaler.referenceResolution = sourceScaler.referenceResolution;
        targetScaler.screenMatchMode = sourceScaler.screenMatchMode;
        targetScaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
        targetScaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
    }

    private static int GetHighestSortingLayerId()
    {
        SortingLayer[] layers = SortingLayer.layers;
        int highestId = 0;
        int highestValue = int.MinValue;

        foreach (SortingLayer layer in layers)
        {
            int value = SortingLayer.GetLayerValueFromID(layer.id);
            if (value <= highestValue)
                continue;

            highestValue = value;
            highestId = layer.id;
        }

        return highestId;
    }

    private void ShowBackground()
    {
        if (backgroundGroup == null)
            CreateBackground();

        if (backgroundGroup == null)
            return;

        backgroundGroup.gameObject.SetActive(true);
        backgroundGroup.alpha = 1f;
        backgroundGroup.blocksRaycasts = true;
    }

    private void HideImmediately()
    {
        if (finishRoutine != null)
        {
            StopCoroutine(finishRoutine);
            finishRoutine = null;
        }

        if (backgroundGroup != null)
            backgroundGroup.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
            startRoutine = null;
        }

        hasStarted = false;
        HideImmediately();
    }
}

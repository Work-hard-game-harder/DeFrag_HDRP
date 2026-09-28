using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(Image))]
public sealed class UISpriteSequencePlayer : MonoBehaviour
{
    [SerializeField] private Image targetImage;
    [SerializeField] private Sprite[] frames;

    [Min(1f)]
    [SerializeField] private float framesPerSecond = 15f;

    [SerializeField] private bool loop = true;
    [Tooltip("독립적으로 사용할 때만 켭니다. 자막 Visual Override에서는 꺼두는 것을 권장합니다.")]
    [SerializeField] private bool playOnEnable;

    private int currentFrame;
    private float elapsedTime;
    private bool isPlaying;

    private void Awake()
    {
        ResolveTargetImage();
    }

    private void OnEnable()
    {
        if (playOnEnable)
            PlayFromBeginning();
    }

    private void Update()
    {
        if (!isPlaying || targetImage == null || frames == null || frames.Length == 0)
            return;

        elapsedTime += Time.unscaledDeltaTime;

        float frameDuration = 1f / framesPerSecond;

        while (elapsedTime >= frameDuration)
        {
            elapsedTime -= frameDuration;
            currentFrame++;

            if (currentFrame >= frames.Length)
            {
                if (!loop)
                {
                    currentFrame = frames.Length - 1;
                    isPlaying = false;
                }
                else
                {
                    currentFrame = 0;
                }
            }

            targetImage.sprite = frames[currentFrame];
        }
    }

    public void PlayFromBeginning()
    {
        ResolveTargetImage();

        if (targetImage == null || frames == null || frames.Length == 0)
            return;

        currentFrame = 0;
        elapsedTime = 0f;
        targetImage.sprite = frames[0];
        targetImage.preserveAspect = true;
        isPlaying = true;
    }

    public void Stop()
    {
        isPlaying = false;
    }

    public void StopAndHide()
    {
        Stop();
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        Stop();
    }

    private void OnValidate()
    {
        ResolveTargetImage();
        framesPerSecond = Mathf.Max(1f, framesPerSecond);

        if (targetImage != null)
        {
            targetImage.preserveAspect = true;
            targetImage.raycastTarget = false;
        }
    }

    private void ResolveTargetImage()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

#if UNITY_EDITOR
    [ContextMenu("Auto Fill Frames From Current Sprite Sheet")]
    private void AutoFillFramesFromCurrentSpriteSheet()
    {
        ResolveTargetImage();

        if (targetImage == null || targetImage.sprite == null)
        {
            Debug.LogWarning(
                $"[{nameof(UISpriteSequencePlayer)}] 먼저 Image의 Source Image에 같은 시트의 Sprite 하나를 지정하세요.",
                this);
            return;
        }

        string assetPath = AssetDatabase.GetAssetPath(targetImage.sprite);
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        var spriteList = new System.Collections.Generic.List<Sprite>(assets.Length);
        foreach (UnityEngine.Object asset in assets)
        {
            if (asset is Sprite sprite)
                spriteList.Add(sprite);
        }

        spriteList.Sort((left, right) =>
            GetTrailingNumber(left.name).CompareTo(GetTrailingNumber(right.name)));

        frames = spriteList.ToArray();
        EditorUtility.SetDirty(this);
        PrefabUtility.RecordPrefabInstancePropertyModifications(this);

        Debug.Log(
            $"[{nameof(UISpriteSequencePlayer)}] {frames.Length}개 프레임을 숫자 순서로 등록했습니다.",
            this);
    }

    private static int GetTrailingNumber(string value)
    {
        if (string.IsNullOrEmpty(value))
            return int.MaxValue;

        int start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1]))
            start--;

        return start < value.Length && int.TryParse(value.Substring(start), out int number)
            ? number
            : int.MaxValue;
    }
#endif
}

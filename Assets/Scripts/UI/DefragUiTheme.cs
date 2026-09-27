using TMPro;
using UnityEngine;

// Single source for runtime-built HUD styling; lives in Resources so code-built UI needs no scene wiring.
[CreateAssetMenu(menuName = "DeFrag/UI Theme", fileName = "DefragUiTheme")]
public sealed class DefragUiTheme : ScriptableObject
{
    private const string ResourcePath = "DefragUiTheme";
    private static DefragUiTheme current;

    [Header("Typography")]
    [Tooltip("한글 글리프가 포함된 터미널 폰트입니다.")]
    public TMP_FontAsset font;

    [Header("Surfaces")]
    public Color backdrop = new(0.004f, 0.018f, 0.02f, 0.94f);
    public Color panel = new(0.012f, 0.05f, 0.052f, 0.95f);
    public Color panelRaised = new(0.02f, 0.09f, 0.085f, 0.97f);
    public Color edge = new(0.25f, 0.95f, 0.8f, 0.55f);

    [Header("Text & Accents")]
    public Color text = new(0.62f, 1f, 0.8f, 1f);
    public Color dim = new(0.28f, 0.55f, 0.46f, 1f);
    public Color accent = new(0.3f, 1f, 0.62f, 1f);
    public Color info = new(1f, 0.7f, 0.22f, 1f);
    public Color danger = new(1f, 0.3f, 0.22f, 1f);
    public Color highlight = new(0.85f, 1f, 0.92f, 1f);

    public static DefragUiTheme Current
    {
        get
        {
            if (current != null)
                return current;
            current = Resources.Load<DefragUiTheme>(ResourcePath);
            if (current == null)
            {
                current = CreateInstance<DefragUiTheme>();
                current.name = "DefragUiTheme (Fallback)";
            }
            return current;
        }
    }

    public static string Hex(Color color) => ColorUtility.ToHtmlStringRGB(color);
}

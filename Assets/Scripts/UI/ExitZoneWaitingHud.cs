using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local presentation for <see cref="CinematicSceneTrigger"/>: while this peer's player stands in an
/// open exit zone and the partner has not arrived yet, a small banner tells them to wait.
/// Reads only replicated state; draws nothing for the other player.
/// </summary>
[RequireComponent(typeof(CinematicSceneTrigger))]
public sealed class ExitZoneWaitingHud : MonoBehaviour
{
    [SerializeField] private string waitingFormat = "비상구 도착  ·  동료를 기다리는 중 ({0}/{1})";
    [SerializeField] private int sortingOrder = 1200;
    [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.25f;

    private CinematicSceneTrigger exit;
    private CanvasGroup group;
    private TMP_Text label;
    private int shownInside = -1, shownRequired = -1;

    private void Awake() => exit = GetComponent<CinematicSceneTrigger>();

    private void Update()
    {
        bool show = exit.IsSpawned && exit.IsOpen && !exit.Started && exit.PlayersRequired > 1 &&
            exit.PlayersInside < exit.PlayersRequired && exit.ContainsLocalPlayer();
        if (show && group == null) Build();
        if (group == null) return;
        group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime / fadeSeconds);
        if (exit.PlayersInside != shownInside || exit.PlayersRequired != shownRequired)
        {
            shownInside = exit.PlayersInside;
            shownRequired = exit.PlayersRequired;
            label.text = string.Format(waitingFormat, shownInside, shownRequired);
        }
    }

    private void Build()
    {
        Canvas canvas = RuntimeUi.Canvas("Exit Zone Waiting HUD", transform, sortingOrder);
        canvas.GetComponent<GraphicRaycaster>().enabled = false;
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        Image panel = RuntimeUi.FramedPanel("Banner", canvas.transform, RuntimeUi.Theme.panel, 14f);
        RuntimeUi.PlaceCentered(panel.rectTransform, new Vector2(0.5f, 0.78f), new Vector2(760f, 64f));
        label = RuntimeUi.Text("Text", panel.transform, 26f, TextAlignmentOptions.Center, null, RuntimeUi.Theme.info);
        RuntimeUi.Stretch(label.rectTransform, 8f);
    }

    private void OnDestroy()
    {
        if (group != null) Destroy(group.gameObject);
    }
}

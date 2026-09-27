using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    // Local HUD for the player at the box: objective strip plus screen-projected switch numbers matching the hacker's monitor.
    public sealed class DistributionOperatorHud : MonoBehaviour
    {
        private const float LabelWorldOffset = 0.1f;

        private DistributionBoxController box;
        private Camera viewCamera;
        private RectTransform canvasRect;
        private TMP_Text strip;
        private readonly TMP_Text[] labels = new TMP_Text[5];

        public static DistributionOperatorHud Create(DistributionBoxController box, Camera viewCamera)
        {
            Canvas canvas = RuntimeUi.Canvas("Distribution Operator HUD", null, 150);
            DistributionOperatorHud hud = canvas.gameObject.AddComponent<DistributionOperatorHud>();
            hud.box = box;
            hud.viewCamera = viewCamera;
            hud.canvasRect = (RectTransform)canvas.transform;
            hud.Build();
            return hud;
        }

        private void Build()
        {
            DefragUiTheme theme = RuntimeUi.Theme;
            Image top = RuntimeUi.FramedPanel("Objective", transform, theme.panel, 14f);
            RuntimeUi.Place(top.rectTransform, new Vector2(0.22f, 0.9f), new Vector2(0.78f, 0.975f));
            strip = RuntimeUi.Text("Text", top.transform, 26f, TextAlignmentOptions.Center);
            RuntimeUi.Stretch(strip.rectTransform, 6f);
            strip.enableAutoSizing = true;
            strip.fontSizeMin = 14f;
            strip.fontSizeMax = 26f;

            Image bottom = RuntimeUi.Panel("Keys", transform, new Color(0f, 0f, 0f, 0.55f));
            RuntimeUi.Place(bottom.rectTransform, new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.07f));
            TMP_Text keys = RuntimeUi.Text("Text", bottom.transform, 22f, TextAlignmentOptions.Center);
            RuntimeUi.Stretch(keys.rectTransform, 4f);
            keys.text = TerminalScreenController.KeyHints(("마우스", "조준"), ("E", "스위치 / 노브"), ("ESC", "나가기"));

            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = RuntimeUi.Text($"Switch {i + 1}", transform, 46f, TextAlignmentOptions.Center, null, theme.info);
                RuntimeUi.PlaceCentered(labels[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(90f, 70f));
                labels[i].text = (i + 1).ToString();
                labels[i].outlineWidth = 0.25f;
                labels[i].outlineColor = Color.black;
            }
        }

        private void LateUpdate()
        {
            if (box == null || viewCamera == null)
                return;

            DistributionPuzzlePhase phase = box.Phase;
            int bank = BankIndexFor(phase);
            strip.text = phase switch
            {
                DistributionPuzzlePhase.WaitingForBankAData or DistributionPuzzlePhase.WaitingForBankBData or DistributionPuzzlePhase.WaitingForBankCData =>
                    $"뱅크 {(char)('A' + bank)} 대기 중  <size=80%>해커가 터미널에서 [데이터 다운로드] → 회로 모니터를 열어야 해요</size>",
                DistributionPuzzlePhase.BankA or DistributionPuzzlePhase.BankB or DistributionPuzzlePhase.BankC =>
                    $"뱅크 {(char)('A' + bank)}  <size=80%>해커에게 \"몇 번을 ON/OFF 할까?\" 물어보고 스위치를 맞추세요</size>",
                DistributionPuzzlePhase.MainKnob =>
                    "메인 노브  <size=80%>해커가 부르는 숫자 구간에 바늘이 오면 [E]!</size>",
                _ => "비상 전력 복구 완료!"
            };

            bool showLabels = bank >= 0 && phase != DistributionPuzzlePhase.MainKnob && phase != DistributionPuzzlePhase.Completed;
            for (int i = 0; i < labels.Length; i++)
            {
                DistributionSwitch target = showLabels ? box.GetSwitch(bank * DistributionBoxController.SwitchesInBank + i) : null;
                Vector2 local = default;
                bool visible = target != null &&
                               TryProject(target.OffEndWorldPosition - viewCamera.transform.right * LabelWorldOffset, out local);
                labels[i].gameObject.SetActive(visible);
                if (visible)
                    labels[i].rectTransform.anchoredPosition = local;
            }
        }

        private bool TryProject(Vector3 world, out Vector2 local)
        {
            local = default;
            Vector3 screen = viewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0f)
                return false;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
        }

        public static int BankIndexFor(DistributionPuzzlePhase phase) => phase switch
        {
            DistributionPuzzlePhase.WaitingForBankAData or DistributionPuzzlePhase.BankA => 0,
            DistributionPuzzlePhase.WaitingForBankBData or DistributionPuzzlePhase.BankB => 1,
            DistributionPuzzlePhase.WaitingForBankCData or DistributionPuzzlePhase.BankC => 2,
            _ => -1
        };
    }
}

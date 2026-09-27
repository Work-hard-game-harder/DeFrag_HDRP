using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    // Control-panel HUD for the hacking-pad player: facility radar, decoy, and the ignition dial.
    public sealed class GeneratorBPanelView : MonoBehaviour
    {
        private static Color Green => RuntimeUi.Theme.accent;
        private static Color Dim => RuntimeUi.Theme.dim;
        private static Color Amber => RuntimeUi.Theme.info;
        private static Color Red => RuntimeUi.Theme.danger;
        private static Color Background => RuntimeUi.Theme.backdrop;

        private GeneratorBController controller;
        private TMP_FontAsset font;
        private FacilityRadarView radar;
        private GameObject scavengeGroup;
        private GameObject ignitionGroup;
        private TMP_Text fuelStatus;
        private TMP_Text decoyStatus;
        private TMP_Text callout;
        private Image sectorImage;
        private RectTransform needle;
        private RectTransform rpmFill;
        private RectTransform bandZone;
        private Image rpmFillImage;
        private Image[] cylinderLamps;
        private int shownSectorDegrees = -1;
        private string feedback;
        private Color feedbackColor;
        private float feedbackUntil;
        private AudioSource uiAudio;

        public static GeneratorBPanelView Create(Transform canvas, GeneratorBController controller)
        {
            Image root = RuntimeUi.Panel("Generator B Panel", canvas, Background);
            RuntimeUi.Place(root.rectTransform, Vector2.zero, Vector2.one);
            root.raycastTarget = true;
            OperationPanelStyle.Frame(root.gameObject);

            GeneratorBPanelView view = root.gameObject.AddComponent<GeneratorBPanelView>();
            view.controller = controller;
            view.font = controller.UiFont;
            view.Build(root.rectTransform);
            return view;
        }

        public void RequestIgnition()
        {
            if (!controller.IsPrimed || controller.IsComplete)
                return;
            if (controller.IgnitionCoolingDown)
            {
                ShowFeedback("재정비 중... 잠시 후 다시", Dim, 0.8f);
                return;
            }
            uiAudio.PlayOneShot(ProceduralSfx.UiTick);
            controller.SubmitIgnition();
        }

        private void Build(RectTransform root)
        {
            uiAudio = gameObject.AddComponent<AudioSource>();
            uiAudio.playOnAwake = false;
            uiAudio.spatialBlend = 0f;

            TMP_Text title = RuntimeUi.Text("Title", root, 34f, TextAlignmentOptions.MidlineLeft, font, Green);
            RuntimeUi.Place(title.rectTransform, new Vector2(0.025f, 0.93f), new Vector2(0.98f, 0.99f));
            title.text = "GENERATOR B // 비상 시동 제어";

            RectTransform radarArea = RuntimeUi.Panel("Radar Area", root, Color.clear).rectTransform;
            RuntimeUi.Place(radarArea, new Vector2(0.02f, 0.08f), new Vector2(0.6f, 0.92f));
            radar = FacilityRadarView.Create(radarArea, controller.RadarSettings, font, new GeneratorBRadarContent(controller));
            radar.MapClicked += OnMapClicked;

            RectTransform column = RuntimeUi.Panel("Right Column", root, new Color(0f, 0.05f, 0.025f, 0.6f)).rectTransform;
            RuntimeUi.Place(column, new Vector2(0.62f, 0.08f), new Vector2(0.98f, 0.92f));
            OperationPanelStyle.Frame(column.gameObject);

            scavengeGroup = BuildScavengeGroup(column);
            ignitionGroup = BuildIgnitionGroup(column);

            decoyStatus = RuntimeUi.Text("Decoy", column, 22f, TextAlignmentOptions.Center, font, Amber);
            RuntimeUi.Place(decoyStatus.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.09f));

            TMP_Text footer = RuntimeUi.Text("Footer", root, 21f, TextAlignmentOptions.MidlineLeft, font, Dim);
            RuntimeUi.Place(footer.rectTransform, new Vector2(0.025f, 0.01f), new Vector2(0.98f, 0.07f));
            footer.text = "[마우스 클릭] 지도 위치에 소음 미끼    [SPACE] 점화    [ESC] 패널에서 나가기";

            controller.DecoyFired += OnDecoyFired;
            controller.IgnitionResolved += OnIgnitionResolved;
        }

        private GameObject BuildScavengeGroup(RectTransform column)
        {
            RectTransform group = RuntimeUi.Panel("Scavenge", column, Color.clear).rectTransform;
            RuntimeUi.Place(group, new Vector2(0f, 0.1f), Vector2.one);

            TMP_Text header = RuntimeUi.Text("Header", group, 30f, TextAlignmentOptions.Center, font, Green);
            RuntimeUi.Place(header.rectTransform, new Vector2(0.04f, 0.86f), new Vector2(0.96f, 0.98f));
            header.text = "1단계 // 연료 수색";

            fuelStatus = RuntimeUi.Text("Fuel", group, 44f, TextAlignmentOptions.Center, font, Amber);
            RuntimeUi.Place(fuelStatus.rectTransform, new Vector2(0.04f, 0.68f), new Vector2(0.96f, 0.84f));

            TMP_Text guide = RuntimeUi.Text("Guide", group, 23f, TextAlignmentOptions.TopLeft, font, Green);
            RuntimeUi.Place(guide.rectTransform, new Vector2(0.07f, 0.05f), new Vector2(0.95f, 0.66f));
            guide.lineSpacing = 18f;
            guide.text =
                "● <color=#5CFF74>초록 화살표</color> = 동료\n" +
                "● <color=#FF9A1F>주황 구역</color> = 연료 신호\n" +
                "   동료가 가까이 가면 정확히 표시됩니다\n" +
                "● <color=#FF3020>빨간 점</color> = 괴물 (3초마다 탐지)\n\n" +
                "무전으로 동료를 연료통까지 안내하고\n발전기 주유구로 데려오세요.";
            return group.gameObject;
        }

        private GameObject BuildIgnitionGroup(RectTransform column)
        {
            RectTransform group = RuntimeUi.Panel("Ignition", column, Color.clear).rectTransform;
            RuntimeUi.Place(group, new Vector2(0f, 0.1f), Vector2.one);

            TMP_Text header = RuntimeUi.Text("Header", group, 30f, TextAlignmentOptions.Center, font, Green);
            RuntimeUi.Place(header.rectTransform, new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f));
            header.text = "2단계 // 점화 시퀀스";

            TMP_Text rule = RuntimeUi.Text("Rule", group, 20f, TextAlignmentOptions.Center, font, Dim);
            RuntimeUi.Place(rule.rectTransform, new Vector2(0.04f, 0.83f), new Vector2(0.96f, 0.9f));
            rule.text = "회전수가 초록 구간일 때, 바늘이 초록 창에 오면 SPACE";

            RectTransform dial = RuntimeUi.Panel("Dial", group, new Color(0.05f, 0.25f, 0.12f, 0.9f), RuntimeUiSprites.Ring).rectTransform;
            RuntimeUi.PlaceCentered(dial, new Vector2(0.42f, 0.56f), new Vector2(300f, 300f));
            sectorImage = RuntimeUi.Panel("Ignition Window", dial, Green);
            RuntimeUi.Place(sectorImage.rectTransform, Vector2.zero, Vector2.one);

            Image needleImage = RuntimeUi.Panel("Needle", dial, Color.white);
            needle = needleImage.rectTransform;
            needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0f);
            needle.sizeDelta = new Vector2(8f, 140f);
            needle.anchoredPosition = Vector2.zero;
            RectTransform hub = RuntimeUi.Panel("Hub", dial, Color.white, RuntimeUiSprites.Disc).rectTransform;
            RuntimeUi.PlaceCentered(hub, new Vector2(0.5f, 0.5f), new Vector2(26f, 26f));

            RectTransform rpmBar = RuntimeUi.Panel("RPM Bar", group, new Color(0.02f, 0.1f, 0.06f, 1f)).rectTransform;
            rpmBar.anchorMin = new Vector2(0.84f, 0.34f);
            rpmBar.anchorMax = new Vector2(0.92f, 0.78f);
            rpmBar.offsetMin = rpmBar.offsetMax = Vector2.zero;
            bandZone = RuntimeUi.Panel("Band", rpmBar, new Color(0.15f, 0.8f, 0.3f, 0.45f)).rectTransform;
            Image fill = RuntimeUi.Panel("Fill", rpmBar, Amber);
            rpmFill = fill.rectTransform;
            rpmFillImage = fill;
            TMP_Text rpmLabel = RuntimeUi.Text("RPM Label", group, 18f, TextAlignmentOptions.Center, font, Dim);
            RuntimeUi.Place(rpmLabel.rectTransform, new Vector2(0.78f, 0.28f), new Vector2(0.98f, 0.34f));
            rpmLabel.text = "회전수";

            cylinderLamps = new Image[controller.RequiredCylinders];
            for (int i = 0; i < cylinderLamps.Length; i++)
            {
                cylinderLamps[i] = RuntimeUi.Panel($"Cylinder {i + 1}", group, Dim, RuntimeUiSprites.Disc);
                float x = 0.5f + (i - (cylinderLamps.Length - 1) * 0.5f) * 0.14f;
                RuntimeUi.PlaceCentered(cylinderLamps[i].rectTransform, new Vector2(x, 0.24f), new Vector2(46f, 46f));
            }

            callout = RuntimeUi.Text("Callout", group, 46f, TextAlignmentOptions.Center, font, Green);
            RuntimeUi.Place(callout.rectTransform, new Vector2(0.02f, 0.0f), new Vector2(0.98f, 0.17f));
            return group.gameObject;
        }

        private void Update()
        {
            if (controller == null)
                return;

            bool primed = controller.IsPrimed;
            if (scavengeGroup.activeSelf == primed) scavengeGroup.SetActive(!primed);
            if (ignitionGroup.activeSelf != primed) ignitionGroup.SetActive(primed);

            fuelStatus.text = $"연료 {controller.ConsumedFuelCans} / {controller.RequiredFuelCans}";
            float decoyCooldown = controller.DecoyCooldownRemaining;
            decoyStatus.text = decoyCooldown <= 0f
                ? "소음 미끼 준비됨 — 지도를 클릭해 괴물을 유인"
                : $"소음 미끼 재충전 {decoyCooldown:0}s";
            decoyStatus.color = decoyCooldown <= 0f ? Amber : Dim;

            if (primed)
                RefreshIgnition();
        }

        private void RefreshIgnition()
        {
            int sector = Mathf.RoundToInt(controller.SectorDegrees);
            if (sector != shownSectorDegrees)
            {
                sectorImage.sprite = RuntimeUiSprites.Wedge(sector);
                shownSectorDegrees = sector;
            }

            float rpm = controller.Rpm;
            Vector2 band = controller.CurrentBand;
            bool inBand = GeneratorBCrankSimulation.IsInBand(rpm, band);
            bool inSector = GeneratorBCrankSimulation.IsInSector(controller.SmoothedAngle, sector);
            bool ready = inBand && !controller.IgnitionCoolingDown;

            needle.localRotation = Quaternion.Euler(0f, 0f, -controller.SmoothedAngle);
            sectorImage.color = ready ? (inSector ? Color.white : Green) : new Color(0.2f, 0.35f, 0.25f, 0.8f);
            bandZone.anchorMin = new Vector2(0f, band.x);
            bandZone.anchorMax = new Vector2(1f, band.y);
            bandZone.offsetMin = bandZone.offsetMax = Vector2.zero;
            rpmFill.anchorMin = Vector2.zero;
            rpmFill.anchorMax = new Vector2(1f, rpm);
            rpmFill.offsetMin = rpmFill.offsetMax = Vector2.zero;
            rpmFillImage.color = inBand ? Green : Amber;

            for (int i = 0; i < cylinderLamps.Length; i++)
                cylinderLamps[i].color = i < controller.IgnitedCylinders ? Green : Dim;

            if (Time.unscaledTime < feedbackUntil)
            {
                callout.text = feedback;
                callout.color = feedbackColor;
                return;
            }

            if (!controller.CrankOperatorPresent)
                SetCallout("동료가 크랭크를 잡아야 합니다\n(주유구에서 E)", Dim);
            else if (controller.IgnitionCoolingDown)
                SetCallout("재정비 중...", Dim);
            else if (rpm < band.x)
                SetCallout("↑ 더 빨리! ↑", Amber);
            else if (rpm > band.y)
                SetCallout("↓ 천천히! ↓", Amber);
            else if (inSector)
                SetCallout(Mathf.Repeat(Time.unscaledTime, 0.3f) < 0.2f ? "지금! SPACE" : string.Empty, Color.white);
            else
                SetCallout("● 유지! 바늘을 기다려", Green);
        }

        private void SetCallout(string text, Color color)
        {
            callout.text = text;
            callout.color = color;
        }

        private void ShowFeedback(string text, Color color, float seconds)
        {
            feedback = text;
            feedbackColor = color;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        private void OnMapClicked(Vector3 world)
        {
            if (controller.DecoyCooldownRemaining > 0f)
            {
                uiAudio.PlayOneShot(ProceduralSfx.UiTick, 0.4f);
                return;
            }
            controller.SubmitDecoy(world);
        }

        private void OnDecoyFired(Vector3 world) => radar.ShowPulse(world, Amber);

        private void OnIgnitionResolved(GeneratorBIgnitionResult result)
        {
            switch (result)
            {
                case GeneratorBIgnitionResult.Hit:
                    ShowFeedback("점화 성공!", Green, 1.2f);
                    break;
                case GeneratorBIgnitionResult.Backfire:
                    ShowFeedback("역화!! 괴물이 들었다", Red, 1.5f);
                    break;
                default:
                    ShowFeedback("회전수부터 맞추세요!", Amber, 1f);
                    break;
            }
        }

        private void OnDestroy()
        {
            if (controller != null)
            {
                controller.DecoyFired -= OnDecoyFired;
                controller.IgnitionResolved -= OnIgnitionResolved;
            }
            if (radar != null)
                radar.MapClicked -= OnMapClicked;
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeFrag.B1F
{
    // Crank HUD: shows effort and ignition progress but never the target RPM, which only the panel sees.
    public sealed class GeneratorBCrankView : MonoBehaviour
    {
        private static Color Green => RuntimeUi.Theme.accent;
        private static Color Dim => RuntimeUi.Theme.dim;
        private static Color Red => RuntimeUi.Theme.danger;
        private static Color KeyIdle => RuntimeUi.Theme.panelRaised;

        private const float KeyFlashSeconds = 0.12f;

        private GeneratorBController controller;
        private TMP_FontAsset font;
        private RectTransform wheel;
        private Image[] keys;
        private TMP_Text[] keyLabels;
        private float[] keyFlashUntil;
        private Image[] cylinderLamps;
        private TMP_Text feedbackText;
        private float feedbackUntil;

        public static GeneratorBCrankView Create(Transform canvas, GeneratorBController controller)
        {
            Image root = RuntimeUi.Panel("Generator B Crank", canvas, new Color(0.005f, 0.02f, 0.012f, 0.82f));
            RuntimeUi.Place(root.rectTransform, new Vector2(0.2f, 0.04f), new Vector2(0.8f, 0.4f));
            OperationPanelStyle.Frame(root.gameObject);

            GeneratorBCrankView view = root.gameObject.AddComponent<GeneratorBCrankView>();
            view.controller = controller;
            view.font = controller.UiFont;
            view.Build(root.rectTransform);
            return view;
        }

        public void FlashKey(int side)
        {
            if (side >= 0 && side < keyFlashUntil.Length)
                keyFlashUntil[side] = Time.unscaledTime + KeyFlashSeconds;
        }

        private void Build(RectTransform root)
        {
            TMP_Text title = RuntimeUi.Text("Title", root, 44f, TextAlignmentOptions.Center, font, Green);
            RuntimeUi.Place(title.rectTransform, new Vector2(0.03f, 0.76f), new Vector2(0.97f, 0.97f));
            title.text = "A  D 를 번갈아 연타!";

            TMP_Text hint = RuntimeUi.Text("Hint", root, 23f, TextAlignmentOptions.Center, font, Green);
            RuntimeUi.Place(hint.rectTransform, new Vector2(0.03f, 0.62f), new Vector2(0.97f, 0.76f));
            hint.text = "속도는 해커가 알려줍니다 — 무전을 잘 들으세요!";

            wheel = RuntimeUi.Panel("Flywheel", root, Green, RuntimeUiSprites.Wheel).rectTransform;
            RuntimeUi.PlaceCentered(wheel, new Vector2(0.5f, 0.33f), new Vector2(150f, 150f));

            keys = new Image[2];
            keyLabels = new TMP_Text[2];
            keyFlashUntil = new float[2];
            string[] labels = { "A", "D" };
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = RuntimeUi.Panel($"Key {labels[i]}", root, KeyIdle);
                RuntimeUi.PlaceCentered(keys[i].rectTransform, new Vector2(i == 0 ? 0.3f : 0.7f, 0.33f), new Vector2(110f, 110f));
                OperationPanelStyle.Frame(keys[i].gameObject);
                TMP_Text keyLabel = RuntimeUi.Text("Label", keys[i].transform, 64f, TextAlignmentOptions.Center, font, Green);
                RuntimeUi.Place(keyLabel.rectTransform, Vector2.zero, Vector2.one);
                keyLabel.text = labels[i];
                keyLabels[i] = keyLabel;
            }

            cylinderLamps = new Image[controller.RequiredCylinders];
            for (int i = 0; i < cylinderLamps.Length; i++)
            {
                cylinderLamps[i] = RuntimeUi.Panel($"Cylinder {i + 1}", root, Dim, RuntimeUiSprites.Disc);
                float x = 0.86f + (i - (cylinderLamps.Length - 1) * 0.5f) * 0.05f;
                RuntimeUi.PlaceCentered(cylinderLamps[i].rectTransform, new Vector2(x, 0.1f), new Vector2(30f, 30f));
            }
            TMP_Text cylinderLabel = RuntimeUi.Text("Cylinder Label", root, 18f, TextAlignmentOptions.Center, font, Dim);
            RuntimeUi.Place(cylinderLabel.rectTransform, new Vector2(0.76f, 0.16f), new Vector2(0.96f, 0.24f));
            cylinderLabel.text = "점화";

            feedbackText = RuntimeUi.Text("Feedback", root, 30f, TextAlignmentOptions.Center, font, Red);
            RuntimeUi.Place(feedbackText.rectTransform, new Vector2(0.03f, 0.02f), new Vector2(0.72f, 0.16f));
            TMP_Text footer = RuntimeUi.Text("Footer", root, 18f, TextAlignmentOptions.TopLeft, font, Dim);
            RuntimeUi.Place(footer.rectTransform, new Vector2(0.015f, 0.88f), new Vector2(0.3f, 0.98f));
            footer.text = "[ESC] 크랭크 놓기";

            controller.IgnitionResolved += OnIgnitionResolved;
        }

        private void Update()
        {
            if (controller == null)
                return;

            wheel.localRotation = Quaternion.Euler(0f, 0f, -controller.SmoothedAngle);
            for (int i = 0; i < keys.Length; i++)
            {
                bool flashing = Time.unscaledTime < keyFlashUntil[i];
                keys[i].color = flashing ? Green : KeyIdle;
                keyLabels[i].color = flashing ? KeyIdle : Green;
            }
            for (int i = 0; i < cylinderLamps.Length; i++)
                cylinderLamps[i].color = i < controller.IgnitedCylinders ? Green : Dim;
            if (Time.unscaledTime >= feedbackUntil)
                feedbackText.text = string.Empty;
        }

        private void OnIgnitionResolved(GeneratorBIgnitionResult result)
        {
            if (result == GeneratorBIgnitionResult.NotReady)
                return;
            bool hit = result == GeneratorBIgnitionResult.Hit;
            feedbackText.text = hit ? "점화 성공! 계속 돌려!" : "역화!! 괴물이 들었다";
            feedbackText.color = hit ? Green : Red;
            feedbackUntil = Time.unscaledTime + 1.5f;
        }

        private void OnDestroy()
        {
            if (controller != null)
                controller.IgnitionResolved -= OnIgnitionResolved;
        }
    }
}

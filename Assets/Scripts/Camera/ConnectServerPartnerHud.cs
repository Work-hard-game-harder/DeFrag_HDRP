using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

// Uplink HUD for the player who is NOT at the terminal: objective strip, role briefings, and the circuit target grid.
[DisallowMultipleComponent]
public sealed class ConnectServerPartnerHud : MonoBehaviour
{
    private static readonly TutorialCard ScoutCard = new()
    {
        Id = "connect.camera",
        Role = "카메라맨 • 서버 연결",
        Title = "릴레이를 찾아 촬영하라",
        Goal = "해커가 알려주는 릴레이 단말기를 IR 카메라로 촬영합니다.",
        Steps = new[]
        {
            ("C  →  우클릭", "카메라를 들고(C) IR 모드(우클릭)를 켜세요."),
            ("", "해커가 무전으로 길을 안내해요. 신호 막대가 찰수록 가까워요."),
            ("좌클릭", "릴레이 번호가 해커가 말한 것과 같은지 확인하고 가운데 맞춰 촬영!")
        }
    };

    private static readonly TutorialCard CircuitCard = new()
    {
        Id = "connect.camera.circuit",
        Role = "카메라맨 • 회로 복원",
        Title = "모양을 말로 전해라",
        Goal = "오른쪽 격자에 칠해진 칸을 해커에게 불러주세요.",
        Steps = new[]
        {
            ("", "이 모양은 당신에게만 보여요. 해커는 조각만 가지고 있어요."),
            ("무전", "\"B2, B3, C3!\"처럼 좌표를 또박또박 불러주세요.")
        }
    };

    private ConnectServerCoordinator coordinator;
    private NetworkObject ownerObject;
    private Canvas canvas;
    private GameObject strip;
    private TMP_Text stripText;
    private GameObject circuitPanel;
    private TMP_Text circuitCells;
    private readonly List<Image> cells = new();
    private int shownSeed;

    private void Awake() => ownerObject = GetComponentInParent<NetworkObject>();

    private void OnEnable()
    {
        ConnectServerCoordinator.LocalInstanceAvailable += Bind;
        Bind(ConnectServerCoordinator.LocalInstance);
    }

    private void OnDisable()
    {
        ConnectServerCoordinator.LocalInstanceAvailable -= Bind;
        coordinator = null;
        SetVisible(false, false);
    }

    private void OnDestroy()
    {
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    private void Bind(ConnectServerCoordinator value) => coordinator = value;

    private void Update()
    {
        if (!IsPartner(out ConnectServerUplinkPhase phase))
        {
            SetVisible(false, false);
            return;
        }

        bool scanning = phase == ConnectServerUplinkPhase.AwaitingOpticalScan;
        bool circuit = phase == ConnectServerUplinkPhase.AwaitingVerification;
        EnsureHud();
        SetVisible(scanning || circuit, circuit);

        if (scanning)
        {
            MinigameTutorial.ShowFloating(ScoutCard);
            stripText.text = $"목표 릴레이  <color=#{DefragUiTheme.Hex(RuntimeUi.Theme.info)}>{ConnectServerRadarContent.ShortId(coordinator.TargetRelayId)}</color>   " +
                             "<size=80%>해커의 길 안내를 듣고 IR 카메라로 촬영하세요</size>";
        }
        else if (circuit)
        {
            MinigameTutorial.ShowFloating(CircuitCard);
            stripText.text = "회로 데이터 수신  <size=80%>오른쪽 칸 좌표를 해커에게 불러주세요 — 릴레이 앞을 지키세요!</size>";
            RefreshCircuit();
        }
    }

    private bool IsPartner(out ConnectServerUplinkPhase phase)
    {
        phase = ConnectServerUplinkPhase.Idle;
        NetworkManager manager = NetworkManager.Singleton;
        if (coordinator == null || !coordinator.IsSpawned || manager == null || !manager.IsListening)
            return false;
        if (ownerObject != null && !ownerObject.IsOwner)
            return false;
        phase = coordinator.Phase;
        return manager.LocalClientId != coordinator.TerminalOperatorClientId;
    }

    private void RefreshCircuit()
    {
        int seed = coordinator.CircuitSeed;
        if (seed == 0 || seed == shownSeed)
            return;
        shownSeed = seed;
        ConnectServerCircuitPuzzle puzzle = ConnectServerCircuitPuzzle.Generate(seed, coordinator.CompletedRounds + 1);
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int cell = 0; cell < cells.Count; cell++)
            cells[cell].color = puzzle.TargetCells.Contains(cell) ? theme.info : new Color(theme.panelRaised.r, theme.panelRaised.g, theme.panelRaised.b, 0.9f);

        // Read top row first, left to right, matching how people scan the grid.
        IEnumerable<int> ordered = puzzle.TargetCells.OrderByDescending(cell => cell / 5).ThenBy(cell => cell % 5);
        circuitCells.text = $"칠해진 칸  <color=#{DefragUiTheme.Hex(theme.info)}>{string.Join("  ", ordered.Select(ConnectServerCircuitPuzzle.CellName))}</color>";
        UiSfx.Play(UiCue.KnobZone);
    }

    private void EnsureHud()
    {
        if (canvas != null)
            return;
        DefragUiTheme theme = RuntimeUi.Theme;
        canvas = RuntimeUi.Canvas("Connect Server Partner HUD", null, 138);

        Image stripPanel = RuntimeUi.FramedPanel("Objective Strip", canvas.transform, theme.panel, 14f);
        RuntimeUi.Place(stripPanel.rectTransform, new Vector2(0.2f, 0.905f), new Vector2(0.8f, 0.975f));
        strip = stripPanel.gameObject;
        stripText = RuntimeUi.Text("Text", stripPanel.transform, 26f, TextAlignmentOptions.Center);
        RuntimeUi.Stretch(stripText.rectTransform, 6f);
        stripText.enableAutoSizing = true;
        stripText.fontSizeMin = 14f;
        stripText.fontSizeMax = 26f;

        Image panel = RuntimeUi.FramedPanel("Circuit Target", canvas.transform, theme.panel, 20f);
        RuntimeUi.Place(panel.rectTransform, new Vector2(0.7f, 0.25f), new Vector2(0.975f, 0.82f));
        circuitPanel = panel.gameObject;

        TMP_Text title = RuntimeUi.Text("Title", panel.transform, 24f, TextAlignmentOptions.Center, null, theme.highlight);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.04f, 0.88f), new Vector2(0.96f, 0.98f));
        title.text = "회로 목표 모양";

        RectTransform frame = RuntimeUi.Panel("Grid Frame", panel.transform, Color.clear).rectTransform;
        RuntimeUi.Place(frame, new Vector2(0.14f, 0.22f), new Vector2(0.94f, 0.82f));
        GameObject gridObject = new("Grid", typeof(RectTransform), typeof(AspectRatioFitter));
        gridObject.transform.SetParent(frame, false);
        AspectRatioFitter fitter = gridObject.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f;
        RectTransform grid = (RectTransform)gridObject.transform;
        for (int cell = 0; cell < 25; cell++)
        {
            int x = cell % 5, y = cell / 5;
            Image square = RuntimeUi.Panel($"Cell {cell}", grid, theme.panelRaised);
            square.rectTransform.anchorMin = new Vector2(x / 5f, y / 5f);
            square.rectTransform.anchorMax = new Vector2((x + 1) / 5f, (y + 1) / 5f);
            square.rectTransform.offsetMin = new Vector2(3f, 3f);
            square.rectTransform.offsetMax = new Vector2(-3f, -3f);
            cells.Add(square);
        }
        ConnectServerCircuitView.AddCoordinateLabels(grid);

        circuitCells = RuntimeUi.Text("Cells", panel.transform, 26f, TextAlignmentOptions.Center);
        RuntimeUi.Place(circuitCells.rectTransform, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.17f));
        circuitCells.enableAutoSizing = true;
        circuitCells.fontSizeMin = 14f;
        circuitCells.fontSizeMax = 26f;
    }

    private void SetVisible(bool showStrip, bool showCircuit)
    {
        if (strip != null && strip.activeSelf != showStrip) strip.SetActive(showStrip);
        if (circuitPanel != null && circuitPanel.activeSelf != showCircuit) circuitPanel.SetActive(showCircuit);
        if (!showCircuit) shownSeed = 0;
    }
}

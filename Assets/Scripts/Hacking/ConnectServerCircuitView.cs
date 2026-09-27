using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Hacker-side circuit board: pieces only. The target shape is described by the camera partner
// unless the assist outline is revealed after a delay or a rejected upload.
public sealed class ConnectServerCircuitView : MonoBehaviour
{
    private RectTransform board;
    private GameObject playArea;
    private RectTransform pieceLayer;
    private TMP_Text overlay;
    private TMP_Text help;
    private Button submitButton;
    private TMP_Text submitLabel;
    private readonly List<Image> cellImages = new();
    private ConnectServerCircuitPuzzle puzzle;
    private readonly List<ConnectServerCircuitPieceView> pieces = new();
    private Action<string> submit;
    private ConnectServerCircuitPieceView selected;
    private bool interactive;
    private bool assistRevealed;
    private float assistAt;

    public void Begin(int seed, int round, float assistDelay, Action<string> onSubmit)
    {
        puzzle = ConnectServerCircuitPuzzle.Generate(seed, round);
        submit = onSubmit;
        assistAt = Time.unscaledTime + assistDelay;
        Build();
        StartCoroutine(LoadingRoutine());
    }

    public void NotifyRejected()
    {
        UiSfx.Play(UiCue.PieceInvalid);
        foreach (ConnectServerCircuitPieceView piece in pieces)
            piece.Flash(RuntimeUi.Theme.danger);
        RevealAssist();
        interactive = true;
        RefreshSubmit();
    }

    private void Update()
    {
        if (!interactive)
            return;
        if (!assistRevealed && Time.unscaledTime >= assistAt)
            RevealAssist();

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;
        if (selected != null && keyboard.qKey.wasPressedThisFrame) RotateSelected(-1);
        else if (selected != null && keyboard.eKey.wasPressedThisFrame) RotateSelected(1);
        else if ((keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) && AllPlaced())
            SubmitPlacement();
    }

    private IEnumerator LoadingRoutine()
    {
        playArea.SetActive(false);
        overlay.text = "회로 데이터 수신 중...";
        overlay.gameObject.SetActive(true);
        yield return new WaitForSecondsRealtime(0.7f);
        overlay.gameObject.SetActive(false);
        playArea.SetActive(true);
        interactive = true;
        RefreshSubmit();
    }

    private void Build()
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        RectTransform root = (RectTransform)transform;
        RuntimeUi.Stretch(root);

        overlay = RuntimeUi.Text("Overlay", transform, 44f, TextAlignmentOptions.Center);
        RuntimeUi.Stretch(overlay.rectTransform);
        overlay.color = theme.highlight;

        Image area = RuntimeUi.FramedPanel("Circuit Play Area", transform, theme.panel, 18f);
        RuntimeUi.Stretch(area.rectTransform, 6f);
        playArea = area.gameObject;

        TMP_Text title = RuntimeUi.Text("Title", area.transform, 24f, TextAlignmentOptions.MidlineLeft);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.04f, 0.9f), new Vector2(0.96f, 0.99f));
        title.text = $"회로 복원  <size=70%><color=#{DefragUiTheme.Hex(theme.dim)}>// 동료가 불러주는 칸에 모듈을 놓으세요</color></size>";

        RectTransform boardFrame = RuntimeUi.Panel("Board Frame", area.transform, Color.clear).rectTransform;
        RuntimeUi.Place(boardFrame, new Vector2(0.09f, 0.17f), new Vector2(0.6f, 0.86f));
        GameObject boardObject = new("5x5 Board", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        boardObject.transform.SetParent(boardFrame, false);
        board = (RectTransform)boardObject.transform;
        boardObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
        AspectRatioFitter fitter = boardObject.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f;

        for (int cell = 0; cell < 25; cell++)
        {
            int x = cell % 5, y = cell / 5;
            Image square = RuntimeUi.Panel($"Cell {ConnectServerCircuitPuzzle.CellName(cell)}", board, theme.panelRaised);
            RectTransform rect = square.rectTransform;
            rect.anchorMin = new Vector2(x / 5f, y / 5f);
            rect.anchorMax = new Vector2((x + 1) / 5f, (y + 1) / 5f);
            rect.offsetMin = new Vector2(3f, 3f);
            rect.offsetMax = new Vector2(-3f, -3f);
            cellImages.Add(square);
        }
        AddCoordinateLabels(board);

        GameObject layer = new("Placed Pieces", typeof(RectTransform));
        layer.transform.SetParent(board, false);
        pieceLayer = (RectTransform)layer.transform;
        RuntimeUi.Stretch(pieceLayer);

        TMP_Text trayTitle = RuntimeUi.Text("Parts", area.transform, 22f, TextAlignmentOptions.Center);
        RuntimeUi.Place(trayTitle.rectTransform, new Vector2(0.66f, 0.8f), new Vector2(0.97f, 0.88f));
        trayTitle.text = "회로 모듈";
        trayTitle.color = theme.dim;

        for (int i = 0; i < puzzle.Pieces.Count; i++)
        {
            GameObject item = new($"Module {i + 1}", typeof(RectTransform), typeof(CanvasGroup), typeof(ConnectServerCircuitPieceView));
            item.transform.SetParent(area.transform, false);
            RectTransform rect = (RectTransform)item.transform;
            TraySlot(rect, i);
            var view = item.GetComponent<ConnectServerCircuitPieceView>();
            view.Initialize(this, i, puzzle.Pieces[i].ShapeIndex, puzzle.Pieces[i].InitialRotation);
            pieces.Add(view);
        }

        GameObject submitObject = new("Submit", typeof(RectTransform), typeof(Image), typeof(Button));
        submitObject.transform.SetParent(area.transform, false);
        RuntimeUi.Place((RectTransform)submitObject.transform, new Vector2(0.67f, 0.05f), new Vector2(0.96f, 0.15f));
        submitButton = submitObject.GetComponent<Button>();
        submitButton.transition = Selectable.Transition.None;
        submitButton.onClick.AddListener(SubmitPlacement);
        submitLabel = RuntimeUi.Text("Label", submitObject.transform, 24f, TextAlignmentOptions.Center);
        RuntimeUi.Stretch(submitLabel.rectTransform, 4f);

        help = RuntimeUi.Text("Help", area.transform, 19f, TextAlignmentOptions.MidlineLeft);
        RuntimeUi.Place(help.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.63f, 0.14f));
        help.color = theme.dim;
        help.text = TerminalScreenController.KeyHints(("드래그", "모듈 놓기"), ("Q/E", "회전"), ("ENTER", "전송"));
        RefreshSubmit();
    }

    public static void TraySlot(RectTransform rect, int index)
    {
        rect.anchorMin = new Vector2(0.7f, 0.58f - index * 0.21f);
        rect.anchorMax = new Vector2(0.93f, 0.77f - index * 0.21f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    public static void AddCoordinateLabels(RectTransform grid)
    {
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int i = 0; i < 5; i++)
        {
            TMP_Text column = RuntimeUi.Text($"Col {i}", grid, 24f, TextAlignmentOptions.Center, null, theme.info);
            column.rectTransform.anchorMin = new Vector2(i / 5f, 1f);
            column.rectTransform.anchorMax = new Vector2((i + 1) / 5f, 1f);
            column.rectTransform.pivot = new Vector2(0.5f, 0f);
            column.rectTransform.sizeDelta = new Vector2(0f, 30f);
            column.rectTransform.anchoredPosition = new Vector2(0f, 4f);
            column.text = ConnectServerCircuitPuzzle.ColumnName(i);

            TMP_Text row = RuntimeUi.Text($"Row {i}", grid, 24f, TextAlignmentOptions.Center, null, theme.info);
            row.rectTransform.anchorMin = new Vector2(0f, i / 5f);
            row.rectTransform.anchorMax = new Vector2(0f, (i + 1) / 5f);
            row.rectTransform.pivot = new Vector2(1f, 0.5f);
            row.rectTransform.sizeDelta = new Vector2(34f, 0f);
            row.rectTransform.anchoredPosition = new Vector2(-6f, 0f);
            row.text = ConnectServerCircuitPuzzle.RowName(i);
        }
    }

    private void RevealAssist()
    {
        if (assistRevealed)
            return;
        assistRevealed = true;
        DefragUiTheme theme = RuntimeUi.Theme;
        for (int cell = 0; cell < cellImages.Count; cell++)
            if (puzzle.TargetCells.Contains(cell))
                cellImages[cell].color = new Color(theme.info.r, theme.info.g, theme.info.b, 0.28f);
        help.text = $"<color=#{DefragUiTheme.Hex(theme.info)}>보조 신호 수신: 주황 칸이 목표 모양입니다</color>";
        UiSfx.Play(UiCue.KnobZone);
    }

    internal void Select(ConnectServerCircuitPieceView piece)
    {
        selected = piece;
        UiSfx.Play(UiCue.PiecePick);
    }

    internal bool TryDrop(ConnectServerCircuitPieceView piece, Vector2 screenPoint)
    {
        if (!interactive || !RectTransformUtility.ScreenPointToLocalPointInRectangle(board, screenPoint, null, out Vector2 local))
            return false;
        Rect rect = board.rect;
        Vector2 normalized = new(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x), Mathf.InverseLerp(rect.yMin, rect.yMax, local.y));
        if (!TryFindClosestValidAnchor(piece, normalized, out int anchor))
        {
            piece.Flash(RuntimeUi.Theme.danger);
            UiSfx.Play(UiCue.PieceInvalid);
            return false;
        }
        piece.PlaceOnBoard(pieceLayer, anchor);
        UiSfx.Play(UiCue.PiecePlace);
        RefreshSubmit();
        return true;
    }

    private bool TryFindClosestValidAnchor(ConnectServerCircuitPieceView piece, Vector2 pointer, out int bestAnchor)
    {
        bestAnchor = -1;
        float bestDistance = float.PositiveInfinity;
        for (int anchor = 0; anchor < 25; anchor++)
        {
            if (!CanPlace(piece, anchor)) continue;
            List<int> cells = ConnectServerCircuitPuzzle.GetCells(piece.ShapeIndex, piece.Rotation, anchor);
            Vector2 center = Vector2.zero;
            foreach (int cell in cells)
                center += new Vector2((cell % 5 + 0.5f) / 5f, (cell / 5 + 0.5f) / 5f);
            center /= cells.Count;
            float distance = (center - pointer).sqrMagnitude;
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            bestAnchor = anchor;
        }
        // Snap only when dropped near a legal spot so modules never jump across the board.
        return bestAnchor >= 0 && bestDistance <= 0.09f;
    }

    private void RotateSelected(int direction)
    {
        int previous = selected.Rotation;
        selected.SetRotation(previous + direction);
        if (selected.Anchor >= 0 && !CanPlace(selected, selected.Anchor))
        {
            selected.SetRotation(previous);
            selected.Flash(RuntimeUi.Theme.danger);
            UiSfx.Play(UiCue.PieceInvalid);
            return;
        }
        if (selected.Anchor >= 0)
            selected.PlaceOnBoard(pieceLayer, selected.Anchor);
        UiSfx.Play(UiCue.PieceRotate);
    }

    // Free placement: only the board edge and other modules block, so the board never leaks the answer.
    private bool CanPlace(ConnectServerCircuitPieceView piece, int anchor)
    {
        List<int> cells = ConnectServerCircuitPuzzle.GetCells(piece.ShapeIndex, piece.Rotation, anchor);
        if (cells == null) return false;
        var occupied = new HashSet<int>();
        foreach (ConnectServerCircuitPieceView other in pieces)
        {
            if (other == piece || other.Anchor < 0) continue;
            List<int> otherCells = ConnectServerCircuitPuzzle.GetCells(other.ShapeIndex, other.Rotation, other.Anchor);
            if (otherCells != null) foreach (int cell in otherCells) occupied.Add(cell);
        }
        foreach (int cell in cells)
            if (occupied.Contains(cell)) return false;
        return true;
    }

    private bool AllPlaced()
    {
        foreach (ConnectServerCircuitPieceView piece in pieces)
            if (piece.Anchor < 0) return false;
        return true;
    }

    internal void RefreshSubmit()
    {
        if (submitButton == null) return;
        DefragUiTheme theme = RuntimeUi.Theme;
        bool ready = interactive && AllPlaced();
        submitButton.interactable = ready;
        submitButton.image.color = ready ? new Color(theme.accent.r, theme.accent.g, theme.accent.b, 0.35f) : theme.panelRaised;
        submitLabel.color = ready ? theme.highlight : theme.dim;
        submitLabel.text = ready ? "회로 전송 [ENTER]" : "모듈을 모두 놓으세요";
    }

    private void SubmitPlacement()
    {
        if (!interactive || !AllPlaced()) return;
        interactive = false;
        RefreshSubmit();
        UiSfx.Play(UiCue.CircuitComplete);
        submit?.Invoke(EncodePlacements());
    }

    private string EncodePlacements()
    {
        var values = new string[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
            values[i] = $"{pieces[i].Anchor},{pieces[i].Rotation}";
        return string.Join(";", values);
    }
}

public sealed class ConnectServerCircuitPieceView : MonoBehaviour,
    IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int ShapeIndex { get; private set; }
    public int Rotation { get; private set; }
    public int Anchor { get; private set; } = -1;
    private ConnectServerCircuitView owner;
    private RectTransform rect;
    private CanvasGroup group;
    private readonly List<Image> blocks = new();
    private Transform trayParent;
    private int index;

    private static Color BlockColor => RuntimeUi.Theme.accent;

    public void Initialize(ConnectServerCircuitView puzzle, int pieceIndex, int shape, int rotation)
    {
        owner = puzzle; index = pieceIndex; ShapeIndex = shape; Rotation = rotation & 3;
        rect = (RectTransform)transform; group = GetComponent<CanvasGroup>(); trayParent = transform.parent;
        RenderCompact();
    }

    public void OnPointerDown(PointerEventData eventData) => owner.Select(this);
    public void OnBeginDrag(PointerEventData eventData)
    {
        Anchor = -1; transform.SetParent(owner.transform, true); group.blocksRaycasts = false;
        owner.RefreshSubmit();
    }
    public void OnDrag(PointerEventData eventData) => rect.position = eventData.position;
    public void OnEndDrag(PointerEventData eventData)
    {
        group.blocksRaycasts = true;
        if (!owner.TryDrop(this, eventData.position)) ReturnToTray();
    }

    public void SetRotation(int value) { Rotation = (value % 4 + 4) % 4; if (Anchor < 0) RenderCompact(); }

    public void PlaceOnBoard(Transform layer, int anchor)
    {
        Anchor = anchor; transform.SetParent(layer, false); rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; RenderBoard(anchor);
    }

    public void Flash(Color color)
    {
        foreach (Image block in blocks) block.color = color;
        CancelInvoke(nameof(RestoreColor)); Invoke(nameof(RestoreColor), 0.25f);
    }

    private void RestoreColor() { foreach (Image block in blocks) block.color = BlockColor; }

    private void ReturnToTray()
    {
        Anchor = -1; transform.SetParent(trayParent, false);
        ConnectServerCircuitView.TraySlot(rect, index);
        RenderCompact();
        owner.RefreshSubmit();
    }

    private void RenderCompact()
    {
        Clear();
        IReadOnlyList<Vector2Int> shape = ConnectServerCircuitPuzzle.GetShape(ShapeIndex);
        var rotated = new List<Vector2Int>();
        int minX = 99, minY = 99, maxX = -99, maxY = -99;
        foreach (Vector2Int point in shape)
        {
            Vector2Int p = Rotation switch { 1 => new(point.y, -point.x), 2 => new(-point.x, -point.y), 3 => new(-point.y, point.x), _ => point };
            rotated.Add(p); minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
        }
        int span = Mathf.Max(maxX - minX + 1, maxY - minY + 1);
        foreach (Vector2Int p in rotated)
            AddBlock(new Vector2((p.x - minX) / (float)span, (p.y - minY) / (float)span),
                new Vector2((p.x - minX + 1) / (float)span, (p.y - minY + 1) / (float)span));
    }

    private void RenderBoard(int anchor)
    {
        Clear();
        List<int> cells = ConnectServerCircuitPuzzle.GetCells(ShapeIndex, Rotation, anchor);
        if (cells == null) return;
        foreach (int cell in cells)
        {
            int x = cell % 5, y = cell / 5;
            AddBlock(new Vector2(x / 5f, y / 5f), new Vector2((x + 1) / 5f, (y + 1) / 5f));
        }
    }

    private void AddBlock(Vector2 min, Vector2 max)
    {
        GameObject block = new("Module Cell", typeof(RectTransform), typeof(Image), typeof(Outline));
        block.transform.SetParent(transform, false); RectTransform b = (RectTransform)block.transform;
        b.anchorMin = min; b.anchorMax = max; b.offsetMin = new Vector2(4f, 4f); b.offsetMax = new Vector2(-4f, -4f);
        Image image = block.GetComponent<Image>(); image.color = BlockColor;
        block.GetComponent<Outline>().effectColor = RuntimeUi.Theme.highlight; blocks.Add(image);
    }

    private void Clear() { foreach (Image block in blocks) if (block != null) Destroy(block.gameObject); blocks.Clear(); }
}

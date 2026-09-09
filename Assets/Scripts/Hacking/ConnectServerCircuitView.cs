using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class ConnectServerCircuitView : MonoBehaviour
{
    private static readonly Color Bright = new(0.55f, 1f, 0.18f, 1f);
    private static readonly Color Target = new(0.18f, 0.62f, 0.08f, 0.72f);
    private static readonly Color Invalid = new(1f, 0.12f, 0.07f, 0.9f);
    private RectTransform board;
    private RectTransform pieceLayer;
    private TMP_Text overlay;
    private TMP_Text help;
    private ConnectServerCircuitPuzzle puzzle;
    private readonly List<ConnectServerCircuitPieceView> pieces = new();
    private Action<string> submit;
    private ConnectServerCircuitPieceView selected;
    private bool interactive;

    public void Begin(int seed, int round, Action<string> onSubmit)
    {
        puzzle = ConnectServerCircuitPuzzle.Generate(seed, round);
        submit = onSubmit;
        Build();
        StartCoroutine(LoadingRoutine());
    }

    private void Update()
    {
        if (!interactive || selected == null || Keyboard.current == null) return;
        if (Keyboard.current.qKey.wasPressedThisFrame) RotateSelected(-1);
        else if (Keyboard.current.eKey.wasPressedThisFrame) RotateSelected(1);
    }

    private IEnumerator LoadingRoutine()
    {
        board.parent.gameObject.SetActive(false);
        help.gameObject.SetActive(false);
        for (int i = 0; i < 2; i++)
        {
            overlay.text = "LOADING...";
            overlay.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(0.28f);
            overlay.gameObject.SetActive(false);
            yield return new WaitForSecondsRealtime(0.18f);
        }
        board.parent.gameObject.SetActive(true);
        help.gameObject.SetActive(true);
        interactive = true;
    }

    private void Build()
    {
        RectTransform root = (RectTransform)transform;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        overlay = MakeText("Loading", transform, 54f, TextAlignmentOptions.Center);
        Stretch(overlay.rectTransform, Vector2.zero, Vector2.zero);

        GameObject playArea = new("Circuit Play Area", typeof(RectTransform), typeof(Image));
        playArea.transform.SetParent(transform, false);
        RectTransform area = (RectTransform)playArea.transform;
        area.anchorMin = new Vector2(0.05f, 0.08f);
        area.anchorMax = new Vector2(0.95f, 0.93f);
        area.offsetMin = area.offsetMax = Vector2.zero;
        playArea.GetComponent<Image>().color = new Color(0.01f, 0.045f, 0.035f, 0.97f);
        OperationPanelStyle.Frame(playArea);

        GameObject boardObject = new("5x5 Target Board", typeof(RectTransform), typeof(Image));
        boardObject.transform.SetParent(area, false);
        board = (RectTransform)boardObject.transform;
        board.anchorMin = new Vector2(0.06f, 0.13f);
        board.anchorMax = new Vector2(0.65f, 0.91f);
        board.offsetMin = board.offsetMax = Vector2.zero;
        boardObject.GetComponent<Image>().color = new Color(0f, 0.025f, 0.02f, 1f);

        for (int cell = 0; cell < 25; cell++)
        {
            int x = cell % 5;
            int y = cell / 5;
            GameObject square = new($"Cell {x},{y}", typeof(RectTransform), typeof(Image), typeof(Outline));
            square.transform.SetParent(board, false);
            RectTransform rect = (RectTransform)square.transform;
            rect.anchorMin = new Vector2(x / 5f, y / 5f);
            rect.anchorMax = new Vector2((x + 1) / 5f, (y + 1) / 5f);
            rect.offsetMin = new Vector2(2f, 2f);
            rect.offsetMax = new Vector2(-2f, -2f);
            square.GetComponent<Image>().color = puzzle.TargetCells.Contains(cell)
                ? Target : new Color(0.02f, 0.09f, 0.07f, 0.9f);
            square.GetComponent<Outline>().effectColor = new Color(0.3f, 1f, 0.3f, 0.3f);
        }

        GameObject layer = new("Placed Pieces", typeof(RectTransform));
        layer.transform.SetParent(board, false);
        pieceLayer = (RectTransform)layer.transform;
        Stretch(pieceLayer, Vector2.zero, Vector2.zero);

        TMP_Text trayTitle = MakeText("Parts", area, 24f, TextAlignmentOptions.Center);
        trayTitle.text = "CIRCUIT MODULES";
        trayTitle.rectTransform.anchorMin = new Vector2(0.69f, 0.82f);
        trayTitle.rectTransform.anchorMax = new Vector2(0.96f, 0.91f);
        trayTitle.rectTransform.offsetMin = trayTitle.rectTransform.offsetMax = Vector2.zero;

        for (int i = 0; i < puzzle.Pieces.Count; i++)
        {
            GameObject item = new($"Module {i + 1}", typeof(RectTransform), typeof(CanvasGroup), typeof(ConnectServerCircuitPieceView));
            item.transform.SetParent(area, false);
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = new Vector2(0.72f, 0.57f - i * 0.23f);
            rect.anchorMax = new Vector2(0.93f, 0.76f - i * 0.23f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = item.GetComponent<ConnectServerCircuitPieceView>();
            view.Initialize(this, i, puzzle.Pieces[i].ShapeIndex, puzzle.Pieces[i].InitialRotation);
            pieces.Add(view);
        }

        help = MakeText("Help", area, 20f, TextAlignmentOptions.BottomLeft);
        help.text = "DRAG MODULES INTO GREEN CELLS\n[Q/E] ROTATE SELECTED MODULE";
        help.rectTransform.anchorMin = new Vector2(0.06f, 0.01f);
        help.rectTransform.anchorMax = new Vector2(0.94f, 0.12f);
        help.rectTransform.offsetMin = help.rectTransform.offsetMax = Vector2.zero;
    }

    internal void Select(ConnectServerCircuitPieceView piece) => selected = piece;

    internal bool TryDrop(ConnectServerCircuitPieceView piece, Vector2 screenPoint)
    {
        if (!interactive || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                board, screenPoint, null, out Vector2 local)) return false;
        Rect rect = board.rect;
        int x = Mathf.FloorToInt(Mathf.InverseLerp(rect.xMin, rect.xMax, local.x) * 5f);
        int y = Mathf.FloorToInt(Mathf.InverseLerp(rect.yMin, rect.yMax, local.y) * 5f);
        int anchor = y * 5 + x;
        if (!CanPlace(piece, anchor)) { piece.Flash(Invalid); return false; }
        piece.PlaceOnBoard(pieceLayer, anchor);
        CheckComplete();
        return true;
    }

    private void RotateSelected(int direction)
    {
        int previous = selected.Rotation;
        selected.SetRotation(previous + direction);
        if (selected.Anchor >= 0 && !CanPlace(selected, selected.Anchor))
        {
            selected.SetRotation(previous);
            selected.Flash(Invalid);
        }
        else if (selected.Anchor >= 0)
        {
            selected.PlaceOnBoard(pieceLayer, selected.Anchor);
            CheckComplete();
        }
    }

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
            if (!puzzle.TargetCells.Contains(cell) || occupied.Contains(cell)) return false;
        return true;
    }

    private void CheckComplete()
    {
        foreach (ConnectServerCircuitPieceView piece in pieces)
            if (piece.Anchor < 0) return;
        var covered = new HashSet<int>();
        foreach (ConnectServerCircuitPieceView piece in pieces)
        {
            List<int> cells = ConnectServerCircuitPuzzle.GetCells(piece.ShapeIndex, piece.Rotation, piece.Anchor);
            if (cells == null) return;
            foreach (int cell in cells) covered.Add(cell);
        }
        if (!covered.SetEquals(puzzle.TargetCells)) return;
        interactive = false;
        StartCoroutine(CompleteRoutine());
    }

    private IEnumerator CompleteRoutine()
    {
        overlay.text = ">> 해킹 완료 <<";
        overlay.gameObject.SetActive(true);
        RectTransform rect = overlay.rectTransform;
        float elapsed = 0f;
        while (elapsed < 0.38f)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / 0.38f), 3f);
            rect.anchoredPosition = Vector2.Lerp(new Vector2(-1100f, 0f), Vector2.zero, t);
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.75f);
        submit?.Invoke(EncodePlacements());
    }

    private string EncodePlacements()
    {
        var values = new string[pieces.Count];
        for (int i = 0; i < pieces.Count; i++)
            values[i] = $"{pieces[i].Anchor},{pieces[i].Rotation}";
        return string.Join(";", values);
    }

    private static TMP_Text MakeText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        GameObject item = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        item.transform.SetParent(parent, false);
        TMP_Text text = item.GetComponent<TMP_Text>();
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = alignment;
        text.color = Bright;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = min; rect.offsetMax = max;
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

    public void Initialize(ConnectServerCircuitView puzzle, int pieceIndex, int shape, int rotation)
    {
        owner = puzzle; index = pieceIndex; ShapeIndex = shape; Rotation = rotation & 3;
        rect = (RectTransform)transform; group = GetComponent<CanvasGroup>(); trayParent = transform.parent;
        RenderCompact();
    }

    public void OnPointerDown(PointerEventData eventData) => owner.Select(this);
    public void OnBeginDrag(PointerEventData eventData)
    {
        owner.Select(this); Anchor = -1; transform.SetParent(owner.transform, true); group.blocksRaycasts = false;
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
        CancelInvoke(nameof(RestoreColor)); Invoke(nameof(RestoreColor), 0.18f);
    }

    private void RestoreColor() { foreach (Image block in blocks) block.color = new Color(0.55f, 1f, 0.18f, 0.92f); }

    private void ReturnToTray()
    {
        Anchor = -1; transform.SetParent(trayParent, false);
        rect.anchorMin = new Vector2(0.72f, 0.57f - index * 0.23f);
        rect.anchorMax = new Vector2(0.93f, 0.76f - index * 0.23f);
        rect.offsetMin = rect.offsetMax = Vector2.zero; RenderCompact();
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
        foreach (Vector2Int p in rotated)
            AddBlock(new Vector2((p.x - minX) / (float)(maxX - minX + 1), (p.y - minY) / (float)(maxY - minY + 1)),
                new Vector2((p.x - minX + 1) / (float)(maxX - minX + 1), (p.y - minY + 1) / (float)(maxY - minY + 1)));
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
        b.anchorMin = min; b.anchorMax = max; b.offsetMin = new Vector2(3f, 3f); b.offsetMax = new Vector2(-3f, -3f);
        Image image = block.GetComponent<Image>(); image.color = new Color(0.55f, 1f, 0.18f, 0.92f);
        block.GetComponent<Outline>().effectColor = Color.white; blocks.Add(image);
    }

    private void Clear() { foreach (Image block in blocks) if (block != null) Destroy(block.gameObject); blocks.Clear(); }
}

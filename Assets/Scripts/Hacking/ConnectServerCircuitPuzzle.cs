using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ConnectServerCircuitPuzzle
{
    public const int BoardSize = 5;

    private static readonly Vector2Int[][] Shapes =
    {
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) },
        new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) },
        new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1) }
    };

    public readonly struct Piece
    {
        public readonly int ShapeIndex;
        public readonly int InitialRotation;
        public Piece(int shapeIndex, int initialRotation)
        {
            ShapeIndex = shapeIndex;
            InitialRotation = initialRotation;
        }
    }

    public IReadOnlyList<Piece> Pieces => pieces;
    public IReadOnlyCollection<int> TargetCells => targetCells;
    private readonly List<Piece> pieces = new();
    private readonly HashSet<int> targetCells = new();

    public static ConnectServerCircuitPuzzle Generate(int seed, int round)
    {
        var puzzle = new ConnectServerCircuitPuzzle();
        var random = new System.Random(seed);
        int pieceCount = Mathf.Clamp(round, 1, 3);
        var occupied = new HashSet<int>();

        for (int piece = 0; piece < pieceCount; piece++)
        {
            bool placed = false;
            for (int attempt = 0; attempt < 200 && !placed; attempt++)
            {
                int shapeIndex = random.Next(Shapes.Length);
                int rotation = random.Next(4);
                int anchor = random.Next(BoardSize * BoardSize);
                List<int> cells = GetCells(shapeIndex, rotation, anchor);
                if (cells == null || cells.Exists(occupied.Contains)) continue;
                foreach (int cell in cells) occupied.Add(cell);
                int initialRotation = (rotation + random.Next(1, 4)) & 3;
                puzzle.pieces.Add(new Piece(shapeIndex, initialRotation));
                placed = true;
            }
        }

        foreach (int cell in occupied) puzzle.targetCells.Add(cell);
        return puzzle;
    }

    public bool Validate(string encodedPlacements)
    {
        string[] entries = (encodedPlacements ?? string.Empty).Split(';');
        if (entries.Length != pieces.Count) return false;
        var covered = new HashSet<int>();
        for (int i = 0; i < entries.Length; i++)
        {
            string[] values = entries[i].Split(',');
            if (values.Length != 2 || !int.TryParse(values[0], out int anchor) ||
                !int.TryParse(values[1], out int rotation)) return false;
            List<int> cells = GetCells(pieces[i].ShapeIndex, rotation, anchor);
            if (cells == null) return false;
            foreach (int cell in cells)
                if (!targetCells.Contains(cell) || !covered.Add(cell)) return false;
        }
        return covered.SetEquals(targetCells);
    }

    public static List<int> GetCells(int shapeIndex, int rotation, int anchor)
    {
        if (shapeIndex < 0 || shapeIndex >= Shapes.Length || anchor < 0 || anchor >= 25)
            return null;
        int ax = anchor % BoardSize;
        int ay = anchor / BoardSize;
        var result = new List<int>(Shapes[shapeIndex].Length);
        foreach (Vector2Int source in Shapes[shapeIndex])
        {
            Vector2Int cell = Rotate(source, rotation & 3);
            int x = ax + cell.x;
            int y = ay + cell.y;
            if (x < 0 || x >= BoardSize || y < 0 || y >= BoardSize) return null;
            result.Add(y * BoardSize + x);
        }
        return result;
    }

    public static IReadOnlyList<Vector2Int> GetShape(int shapeIndex) => Shapes[shapeIndex];

    private static Vector2Int Rotate(Vector2Int point, int rotation) => rotation switch
    {
        1 => new Vector2Int(point.y, -point.x),
        2 => new Vector2Int(-point.x, -point.y),
        3 => new Vector2Int(-point.y, point.x),
        _ => point
    };
}

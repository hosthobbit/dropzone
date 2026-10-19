using System;
using System.Collections.Generic;
using System.Text;

namespace DropZone.Core
{
    public enum Player : byte { None = 0, One = 1, Two = 2 }

    public static class PlayerExtensions
    {
        public static Player Other(this Player p) => p == Player.One ? Player.Two : p == Player.Two ? Player.One : Player.None;
    }

    /// <summary>A board position. Row 0 is the bottom row.</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public readonly int Column, Row;
        public Cell(int column, int row) { Column = column; Row = row; }
        public bool IsAdjacentTo(Cell o) => Math.Abs(Column - o.Column) + Math.Abs(Row - o.Row) == 1;
        public bool Equals(Cell o) => Column == o.Column && Row == o.Row;
        public override bool Equals(object obj) => obj is Cell c && Equals(c);
        public override int GetHashCode() => Column * 16 + Row;
        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
        public override string ToString() => $"({Column},{Row})";
    }

    /// <summary>Four or more connected counters of one player.</summary>
    public sealed class WinLine
    {
        public Player Owner;
        public Cell[] Cells;
        public Cell First => Cells[0];
        public Cell Last => Cells[Cells.Length - 1];
    }

    /// <summary>
    /// The authoritative logical board: 7 columns by 6 rows with gravity. Counters in a column are
    /// always stacked from the bottom with no gaps; every operation keeps that true.
    /// </summary>
    public sealed class Board
    {
        public const int Columns = 7, Rows = 6, Connect = 4;
        readonly Player[] cells = new Player[Columns * Rows];

        public Player this[int column, int row]
        {
            get => cells[row * Columns + column];
            private set => cells[row * Columns + column] = value;
        }
        public Player this[Cell c] => this[c.Column, c.Row];

        public static bool InBounds(int column, int row) => column >= 0 && column < Columns && row >= 0 && row < Rows;
        public static bool InBounds(Cell c) => InBounds(c.Column, c.Row);

        /// <summary>Number of counters in a column (also the row a dropped counter lands on).</summary>
        public int Height(int column)
        {
            int h = 0;
            while (h < Rows && this[column, h] != Player.None) h++;
            return h;
        }

        public bool CanDrop(int column) => column >= 0 && column < Columns && this[column, Rows - 1] == Player.None;

        public bool IsFull
        {
            get { for (int c = 0; c < Columns; c++) if (CanDrop(c)) return false; return true; }
        }

        public bool IsEmpty
        {
            get { foreach (var p in cells) if (p != Player.None) return false; return true; }
        }

        public int Count(Player p)
        {
            int n = 0;
            foreach (var x in cells) if (x == p) n++;
            return n;
        }

        /// <summary>Drop a counter; returns the row it lands on.</summary>
        public int Drop(int column, Player p)
        {
            if (!CanDrop(column)) throw new InvalidOperationException($"Column {column} is full or out of range");
            if (p == Player.None) throw new ArgumentException("Can't drop an empty counter");
            int row = Height(column);
            this[column, row] = p;
            return row;
        }

        /// <summary>Remove one counter; everything above it in the column falls one row.</summary>
        public void RemoveAndSettle(Cell c)
        {
            if (!InBounds(c) || this[c] == Player.None) throw new InvalidOperationException($"No counter at {c}");
            for (int r = c.Row; r < Rows - 1; r++) this[c.Column, r] = this[c.Column, r + 1];
            this[c.Column, Rows - 1] = Player.None;
        }

        /// <summary>Exchange the owners of two cells.</summary>
        public void Exchange(Cell a, Cell b)
        {
            var t = this[a];
            this[a.Column, a.Row] = this[b];
            this[b.Column, b.Row] = t;
        }

        public void Set(int column, int row, Player p) => this[column, row] = p; // tests and tools; callers keep gravity
        public void Clear() => Array.Clear(cells, 0, cells.Length);

        public Board Clone()
        {
            var b = new Board();
            Array.Copy(cells, b.cells, cells.Length);
            return b;
        }

        public void CopyFrom(Board other) => Array.Copy(other.cells, cells, cells.Length);

        static readonly int[,] Directions = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 } };

        /// <summary>Every maximal run of four or more for a player.</summary>
        public List<WinLine> FindLines(Player p, List<WinLine> into = null)
        {
            into = into ?? new List<WinLine>();
            for (int d = 0; d < 4; d++)
            {
                int dc = Directions[d, 0], dr = Directions[d, 1];
                for (int c = 0; c < Columns; c++)
                    for (int r = 0; r < Rows; r++)
                    {
                        if (this[c, r] != p) continue;
                        // only start at the beginning of a run
                        int pc = c - dc, pr = r - dr;
                        if (InBounds(pc, pr) && this[pc, pr] == p) continue;
                        int len = 0;
                        while (InBounds(c + dc * len, r + dr * len) && this[c + dc * len, r + dr * len] == p) len++;
                        if (len < Connect) continue;
                        var line = new WinLine { Owner = p, Cells = new Cell[len] };
                        for (int i = 0; i < len; i++) line.Cells[i] = new Cell(c + dc * i, r + dr * i);
                        into.Add(line);
                    }
            }
            return into;
        }

        public bool HasLine(Player p) => FindLines(p).Count > 0;

        /// <summary>
        /// Build a board from text rows, top row first. '.' empty, 'X' or '1' player one, 'O' or '2' player two.
        /// Rows can be omitted from the top. Used by tests.
        /// </summary>
        public static Board Parse(params string[] topToBottom)
        {
            var b = new Board();
            int n = topToBottom.Length;
            for (int i = 0; i < n; i++)
            {
                int row = n - 1 - i;
                string s = topToBottom[i].Replace(" ", "");
                for (int c = 0; c < Columns && c < s.Length; c++)
                {
                    char ch = s[c];
                    b[c, row] = ch == 'X' || ch == '1' ? Player.One : ch == 'O' || ch == '2' ? Player.Two : Player.None;
                }
            }
            return b;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int r = Rows - 1; r >= 0; r--)
            {
                for (int c = 0; c < Columns; c++) sb.Append(this[c, r] == Player.One ? 'X' : this[c, r] == Player.Two ? 'O' : '.');
                if (r > 0) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}

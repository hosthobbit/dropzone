using System;
using System.Collections.Generic;

namespace DropZone.Core
{
    public enum GameMode { Classic, Battle }
    public enum ActionKind : byte { Drop, Bomb, Swap }
    public enum RoundOutcome { InProgress, PlayerOneWins, PlayerTwoWins, Draw }

    public sealed class RuleSet
    {
        public GameMode Mode = GameMode.Classic;
        /// <summary>Seconds per turn; 0 = no timer.</summary>
        public int TurnSeconds;
        public int BombsPerRound = 1;
        public int SwapsPerRound = 1;
        public int RoundsToWin = 3;
        public bool PowersEnabled => Mode == GameMode.Battle;

        public static RuleSet Classic(int turnSeconds = 0) => new RuleSet { Mode = GameMode.Classic, TurnSeconds = turnSeconds };
        public static RuleSet Battle(int turnSeconds = 10) => new RuleSet { Mode = GameMode.Battle, TurnSeconds = turnSeconds };
        public RuleSet Clone() => (RuleSet)MemberwiseClone();
    }

    /// <summary>A move, validated by RoundState before it's applied.</summary>
    public readonly struct GameAction : IEquatable<GameAction>
    {
        public readonly ActionKind Kind;
        public readonly Cell A, B; // Drop: A.Column; Bomb: A; Swap: A and B

        GameAction(ActionKind kind, Cell a, Cell b) { Kind = kind; A = a; B = b; }
        public static GameAction Drop(int column) => new GameAction(ActionKind.Drop, new Cell(column, -1), default);
        public static GameAction Bomb(Cell target) => new GameAction(ActionKind.Bomb, target, default);
        public static GameAction Bomb(int column, int row) => Bomb(new Cell(column, row));
        public static GameAction Swap(Cell a, Cell b) => new GameAction(ActionKind.Swap, a, b);
        public int Column => A.Column;
        public bool IsPower => Kind != ActionKind.Drop;

        public bool Equals(GameAction o)
        {
            if (Kind != o.Kind) return false;
            switch (Kind)
            {
                case ActionKind.Drop: return A.Column == o.A.Column;
                case ActionKind.Bomb: return A == o.A;
                default: return (A == o.A && B == o.B) || (A == o.B && B == o.A);
            }
        }
        public override bool Equals(object obj) => obj is GameAction a && Equals(a);
        public override int GetHashCode() => (int)Kind * 10007 + A.GetHashCode() * 31 + (Kind == ActionKind.Swap ? B.GetHashCode() : 0);
        public override string ToString() => Kind == ActionKind.Drop ? $"Drop {A.Column}" : Kind == ActionKind.Bomb ? $"Bomb {A}" : $"Swap {A}<->{B}";
    }

    /// <summary>What a committed action did, for animation and feedback.</summary>
    public sealed class MoveResult
    {
        public GameAction Action;
        public Player By;
        public bool Timeout;
        /// <summary>Drop: the landing cell. Bomb: the removed cell. Swap: first cell.</summary>
        public Cell Cell;
        /// <summary>Bomb: owner of the removed counter.</summary>
        public Player Removed;
        /// <summary>Bomb: how many counters above the removed one fell a row.</summary>
        public int Fallen;
        public RoundOutcome Outcome;
        public List<WinLine> Lines = new List<WinLine>();
        /// <summary>The winner's line was made by the opponent's action (a Bomb or Swap backfired).</summary>
        public bool GiftedWin;
    }

    /// <summary>
    /// One round: the single authoritative state of the board, whose turn it is, and the powers left.
    /// Every change goes through Apply, which validates the action, resolves it and checks both
    /// players for winning lines on the settled board.
    /// </summary>
    public sealed class RoundState
    {
        public static readonly int[] CentreFirst = { 3, 2, 4, 1, 5, 0, 6 };

        public readonly RuleSet Rules;
        public readonly Board Board;
        public Player Current { get; private set; }
        public Player Starter { get; private set; }
        public RoundOutcome Outcome { get; private set; } = RoundOutcome.InProgress;
        public readonly List<WinLine> WinningLines = new List<WinLine>();
        public int MovesMade { get; private set; }
        readonly int[] bombs = new int[3], swaps = new int[3];

        public RoundState(RuleSet rules, Player starter, Board board = null)
        {
            if (starter == Player.None) throw new ArgumentException("A round needs a starting player");
            Rules = rules;
            Board = board ?? new Board();
            Current = Starter = starter;
            int b = rules.PowersEnabled ? rules.BombsPerRound : 0, s = rules.PowersEnabled ? rules.SwapsPerRound : 0;
            bombs[1] = bombs[2] = b;
            swaps[1] = swaps[2] = s;
        }

        public bool IsOver => Outcome != RoundOutcome.InProgress;
        public Player Winner => Outcome == RoundOutcome.PlayerOneWins ? Player.One : Outcome == RoundOutcome.PlayerTwoWins ? Player.Two : Player.None;
        public int BombsLeft(Player p) => bombs[(int)p];
        public int SwapsLeft(Player p) => swaps[(int)p];

        public RoundState Clone()
        {
            var r = new RoundState(Rules, Starter, Board.Clone()) { Current = Current, Outcome = Outcome, MovesMade = MovesMade };
            Array.Copy(bombs, r.bombs, 3);
            Array.Copy(swaps, r.swaps, 3);
            r.WinningLines.AddRange(WinningLines);
            return r;
        }

        // ---------- availability (with reasons to show the player) ----------

        public bool CanBomb(Player p, out string why)
        {
            why = null;
            if (!Rules.PowersEnabled) why = "Powers are only used in Battle.";
            else if (IsOver) why = "The round is over.";
            else if (bombs[(int)p] <= 0) why = "Bomb already used this round.";
            else if (Board.Count(p.Other()) == 0) why = "No opponent counters to bomb yet.";
            return why == null;
        }

        public bool CanSwap(Player p, out string why)
        {
            why = null;
            if (!Rules.PowersEnabled) why = "Powers are only used in Battle.";
            else if (IsOver) why = "The round is over.";
            else if (swaps[(int)p] <= 0) why = "Swap already used this round.";
            else if (!AnySwapPair()) why = "No side-by-side pair of opposing counters to swap.";
            return why == null;
        }

        bool AnySwapPair()
        {
            for (int c = 0; c < Board.Columns; c++)
                for (int r = 0; r < Board.Rows; r++)
                {
                    var p = Board[c, r];
                    if (p == Player.None) continue;
                    if (c + 1 < Board.Columns && Board[c + 1, r] == p.Other()) return true;
                    if (r + 1 < Board.Rows && Board[c, r + 1] == p.Other()) return true;
                }
            return false;
        }

        /// <summary>Is this action legal for the player whose turn it is? `reason` explains a refusal.</summary>
        public bool IsLegal(GameAction a, out string reason)
        {
            reason = null;
            if (IsOver) { reason = "The round is over."; return false; }
            var me = Current;
            switch (a.Kind)
            {
                case ActionKind.Drop:
                    if (a.Column < 0 || a.Column >= Board.Columns) reason = "There's no column there.";
                    else if (!Board.CanDrop(a.Column)) reason = "That column is full.";
                    break;
                case ActionKind.Bomb:
                    if (!CanBomb(me, out reason)) break;
                    if (!Board.InBounds(a.A)) reason = "Pick a counter on the board.";
                    else if (Board[a.A] == Player.None) reason = "That space is empty: bomb an opponent counter.";
                    else if (Board[a.A] == me) reason = "You can't bomb your own counter.";
                    break;
                case ActionKind.Swap:
                    if (!CanSwap(me, out reason)) break;
                    if (!Board.InBounds(a.A) || !Board.InBounds(a.B)) reason = "Pick two counters on the board.";
                    else if (!a.A.IsAdjacentTo(a.B)) reason = "Swap two counters that touch side by side or above and below (not diagonally).";
                    else if (Board[a.A] == Player.None || Board[a.B] == Player.None) reason = "Both spaces need counters in them.";
                    else if (Board[a.A] == Board[a.B]) reason = "Swap one of your counters with an opponent's.";
                    break;
                default: reason = "Unknown action."; break;
            }
            return reason == null;
        }

        public bool IsLegal(GameAction a) => IsLegal(a, out _);

        /// <summary>All legal actions for the current player.</summary>
        public List<GameAction> LegalActions(List<GameAction> into = null, bool includePowers = true)
        {
            into = into ?? new List<GameAction>();
            into.Clear();
            if (IsOver) return into;
            foreach (int c in CentreFirst) if (Board.CanDrop(c)) into.Add(GameAction.Drop(c));
            if (!includePowers) return into;
            var me = Current;
            if (CanBomb(me, out _))
                for (int c = 0; c < Board.Columns; c++)
                    for (int r = 0; r < Board.Rows; r++)
                        if (Board[c, r] == me.Other()) into.Add(GameAction.Bomb(c, r));
            if (CanSwap(me, out _))
                for (int c = 0; c < Board.Columns; c++)
                    for (int r = 0; r < Board.Rows; r++)
                    {
                        var p = Board[c, r];
                        if (p == Player.None) continue;
                        if (c + 1 < Board.Columns && Board[c + 1, r] == p.Other()) into.Add(GameAction.Swap(new Cell(c, r), new Cell(c + 1, r)));
                        if (r + 1 < Board.Rows && Board[c, r + 1] == p.Other()) into.Add(GameAction.Swap(new Cell(c, r), new Cell(c, r + 1)));
                    }
            return into;
        }

        /// <summary>The automatic move when the turn timer runs out: a normal drop, centre column first. Never a power.</summary>
        public GameAction TimeoutAction()
        {
            foreach (int c in CentreFirst) if (Board.CanDrop(c)) return GameAction.Drop(c);
            throw new InvalidOperationException("No column to drop into");
        }

        /// <summary>Apply a legal action for the current player, settle the board and resolve the round.</summary>
        public MoveResult Apply(GameAction a, bool timeout = false)
        {
            if (!IsLegal(a, out string reason)) throw new InvalidOperationException($"Illegal action {a}: {reason}");
            var me = Current;
            var res = new MoveResult { Action = a, By = me, Timeout = timeout };
            switch (a.Kind)
            {
                case ActionKind.Drop:
                    res.Cell = new Cell(a.Column, Board.Drop(a.Column, me));
                    break;
                case ActionKind.Bomb:
                    res.Cell = a.A;
                    res.Removed = Board[a.A];
                    res.Fallen = Board.Height(a.A.Column) - 1 - a.A.Row;
                    Board.RemoveAndSettle(a.A);
                    bombs[(int)me]--;
                    break;
                case ActionKind.Swap:
                    res.Cell = a.A;
                    Board.Exchange(a.A, a.B);
                    swaps[(int)me]--;
                    break;
            }
            MovesMade++;
            Resolve(res);
            return res;
        }

        void Resolve(MoveResult res)
        {
            var one = Board.FindLines(Player.One);
            var two = Board.FindLines(Player.Two);
            WinningLines.Clear();
            WinningLines.AddRange(one);
            WinningLines.AddRange(two);
            if (one.Count > 0 && two.Count > 0) Outcome = RoundOutcome.Draw;
            else if (one.Count > 0) Outcome = RoundOutcome.PlayerOneWins;
            else if (two.Count > 0) Outcome = RoundOutcome.PlayerTwoWins;
            else if (Board.IsFull) Outcome = RoundOutcome.Draw;
            else Current = Current.Other();
            res.Outcome = Outcome;
            res.Lines.AddRange(WinningLines);
            res.GiftedWin = Winner != Player.None && Winner != res.By;
        }
    }
}

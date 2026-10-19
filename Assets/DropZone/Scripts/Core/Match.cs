using System;
using System.Collections.Generic;

namespace DropZone.Core
{
    /// <summary>First to N round wins. Draws award nothing. Starting player alternates every round.</summary>
    public sealed class Match
    {
        public readonly RuleSet Rules;
        public readonly Player FirstStarter;
        readonly int[] wins = new int[3];
        public readonly List<RoundOutcome> History = new List<RoundOutcome>();
        public RoundState Round { get; private set; }
        public int RoundNumber { get; private set; } // 1-based once started

        public Match(RuleSet rules, Player firstStarter)
        {
            Rules = rules;
            FirstStarter = firstStarter == Player.None ? Player.One : firstStarter;
        }

        public int Wins(Player p) => wins[(int)p];
        public bool IsOver => wins[1] >= Rules.RoundsToWin || wins[2] >= Rules.RoundsToWin;
        public Player Winner => wins[1] >= Rules.RoundsToWin ? Player.One : wins[2] >= Rules.RoundsToWin ? Player.Two : Player.None;

        /// <summary>Who starts a given round (1-based): the first starter, then alternating.</summary>
        public Player StarterFor(int round) => round % 2 == 1 ? FirstStarter : FirstStarter.Other();

        public RoundState StartRound()
        {
            if (IsOver) throw new InvalidOperationException("The match is over");
            if (Round != null && !Round.IsOver) throw new InvalidOperationException("The current round hasn't finished");
            RoundNumber++;
            Round = new RoundState(Rules, StarterFor(RoundNumber));
            return Round;
        }

        /// <summary>Count the finished round. Call once per round.</summary>
        public void RecordRound()
        {
            if (Round == null || !Round.IsOver) throw new InvalidOperationException("No finished round to record");
            if (History.Count >= RoundNumber) throw new InvalidOperationException("Round already recorded");
            History.Add(Round.Outcome);
            if (Round.Winner != Player.None) wins[(int)Round.Winner]++;
        }
    }

    /// <summary>Turn timer. Runs only while the player can act; frozen during animations and pause.</summary>
    public sealed class TurnClock
    {
        public float Limit { get; private set; }
        public float Remaining { get; private set; }
        public bool Running { get; private set; }
        public bool Enabled => Limit > 0f;
        public bool Expired => Enabled && Remaining <= 0f;

        public void Reset(float limitSeconds) { Limit = Math.Max(0f, limitSeconds); Remaining = Limit; Running = false; }
        public void Resume() { if (Enabled && !Expired) Running = true; }
        public void Freeze() => Running = false;

        /// <summary>Advance; returns true once, on the tick the time runs out.</summary>
        public bool Tick(float dt)
        {
            if (!Running || !Enabled) return false;
            Remaining = Math.Max(0f, Remaining - dt);
            if (Remaining > 0f) return false;
            Running = false;
            return true;
        }

        /// <summary>Whole seconds shown on screen (rounded up), for the countdown beeps.</summary>
        public int DisplaySeconds => (int)Math.Ceiling(Remaining);
    }

    /// <summary>
    /// What the player is in the middle of choosing: a column, a Bomb target or the two cells of a
    /// Swap. Nothing is committed (no power spent, no turn used) until Confirm returns an action
    /// that RoundState accepts; Cancel always leaves the turn exactly as it was.
    /// </summary>
    public sealed class TurnInput
    {
        public enum Stage { Column, BombTarget, BombConfirm, SwapFirst, SwapSecond, SwapConfirm }

        public Stage Current { get; private set; } = Stage.Column;
        public int Column { get; private set; } = 3;
        public Cell First { get; private set; }
        public Cell Second { get; private set; }
        public string Message { get; private set; }

        public bool Targeting => Current != Stage.Column;
        public bool AwaitingConfirm => Current == Stage.BombConfirm || Current == Stage.SwapConfirm;

        public void Reset(int column = 3) { Current = Stage.Column; Column = column; Message = null; }
        public void SelectColumn(int c) { if (c >= 0 && c < Board.Columns) Column = c; }

        public bool BeginBomb(RoundState s)
        {
            if (!s.CanBomb(s.Current, out string why)) { Message = why; return false; }
            Current = Stage.BombTarget; Message = "Pick an opponent counter to bomb.";
            return true;
        }

        public bool BeginSwap(RoundState s)
        {
            if (!s.CanSwap(s.Current, out string why)) { Message = why; return false; }
            Current = Stage.SwapFirst; Message = "Pick the first counter to swap.";
            return true;
        }

        /// <summary>Choose a cell while targeting. Returns false (with Message) if it can't be used.</summary>
        public bool PickCell(RoundState s, Cell c)
        {
            switch (Current)
            {
                case Stage.BombTarget:
                case Stage.BombConfirm:
                    if (!s.IsLegal(GameAction.Bomb(c), out string why)) { Message = why; return false; }
                    First = c; Current = Stage.BombConfirm; Message = "Confirm to bomb this counter.";
                    return true;
                case Stage.SwapFirst:
                    if (!Board.InBounds(c) || s.Board[c] == Player.None) { Message = "Pick a counter, not an empty space."; return false; }
                    if (!HasPartner(s, c)) { Message = "That counter has no opposing neighbour to swap with."; return false; }
                    First = c; Current = Stage.SwapSecond; Message = "Now pick a touching opponent counter.";
                    return true;
                case Stage.SwapSecond:
                case Stage.SwapConfirm:
                    if (c == First) { Current = Stage.SwapFirst; Message = "Pick the first counter to swap."; return true; }
                    if (!s.IsLegal(GameAction.Swap(First, c), out string why2)) { Message = why2; return false; }
                    Second = c; Current = Stage.SwapConfirm; Message = "Confirm to swap these counters.";
                    return true;
            }
            return false;
        }

        static bool HasPartner(RoundState s, Cell c)
        {
            var other = s.Board[c].Other();
            int[,] d = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            for (int i = 0; i < 4; i++)
            {
                var n = new Cell(c.Column + d[i, 0], c.Row + d[i, 1]);
                if (Board.InBounds(n) && s.Board[n] == other) return true;
            }
            return false;
        }

        /// <summary>The action to commit, if the selection is complete and legal.</summary>
        public bool TryConfirm(RoundState s, out GameAction action)
        {
            action = Current == Stage.Column ? GameAction.Drop(Column)
                   : Current == Stage.BombConfirm ? GameAction.Bomb(First)
                   : Current == Stage.SwapConfirm ? GameAction.Swap(First, Second)
                   : default;
            bool complete = Current == Stage.Column || AwaitingConfirm;
            if (!complete) { Message = Current == Stage.BombTarget ? "Pick a counter to bomb first." : "Pick two counters to swap first."; return false; }
            if (!s.IsLegal(action, out string why)) { Message = why; return false; }
            return true;
        }

        /// <summary>Step back one stage. Returns false if there was nothing to cancel.</summary>
        public bool Cancel()
        {
            switch (Current)
            {
                case Stage.Column: return false;
                case Stage.SwapConfirm: Current = Stage.SwapSecond; Message = "Now pick a touching opponent counter."; return true;
                case Stage.SwapSecond: Current = Stage.SwapFirst; Message = "Pick the first counter to swap."; return true;
                case Stage.BombConfirm: Current = Stage.BombTarget; Message = "Pick an opponent counter to bomb."; return true;
                default: Current = Stage.Column; Message = "Cancelled: nothing used."; return true;
            }
        }

        /// <summary>Drop the whole power selection at once (e.g. the Cancel button).</summary>
        public void CancelAll() { if (Current != Stage.Column) { Current = Stage.Column; Message = "Cancelled: nothing used."; } }
    }
}

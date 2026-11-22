using System;
using System.Collections.Generic;
using System.Linq;

namespace GlyphFinder
{
    internal sealed class TerminalTrial
    {
        public readonly int Guess, Mismatched;

        public TerminalTrial(int guess, int mismatched)
        {
            Guess = guess;
            Mismatched = mismatched;
        }
    }

    internal sealed class TerminalPuzzle
    {
        public const int FieldCount = 4, CombinationCount = 81;
        public static readonly string[][] Options =
        {
            new[] { "v1.0", "v1.1", "v2.0" },
            new[] { "GET", "PUT", "POST" },
            new[] { "LTE", "Fiber", "Sat" },
            new[] { "AES", "RSA", "DES" }
        };

        private static readonly int[] All = Enumerable.Range(0, CombinationCount).ToArray();
        private static readonly int[] Places = { 27, 9, 3, 1 };
        private static readonly byte[,] Differences = BuildDifferences();
        private readonly List<TerminalTrial> trials = new List<TerminalTrial>();
        private int[] remaining = All;

        public IReadOnlyList<TerminalTrial> Trials { get { return trials.AsReadOnly(); } }
        public IReadOnlyList<int> Remaining { get { return Array.AsReadOnly(remaining); } }
        public bool Solved { get { return trials.Count != 0 && trials[trials.Count - 1].Mismatched == 0; } }

        public static int OptionAt(int combination, int field)
        {
            return combination / Places[field] % 3;
        }

        public static string Format(int combination, string separator = "  ")
        {
            return string.Join(separator, Options.Select((choices, field) => choices[OptionAt(combination, field)]));
        }

        public bool Record(int guess, int mismatched)
        {
            if (Solved || guess < 0 || guess >= CombinationCount || mismatched < 0 || mismatched > FieldCount) return false;
            int[] narrowed = remaining.Where(candidate => Differences[guess, candidate] == mismatched).ToArray();
            if (narrowed.Length == 0) return false;
            trials.Add(new TerminalTrial(guess, mismatched));
            remaining = narrowed;
            return true;
        }

        public TerminalTrial Undo()
        {
            if (trials.Count == 0) return null;
            TerminalTrial last = trials[trials.Count - 1];
            trials.RemoveAt(trials.Count - 1);
            remaining = All.Where(candidate => trials.All(trial =>
                Differences[trial.Guess, candidate] == trial.Mismatched)).ToArray();
            return last;
        }

        public void Reset()
        {
            trials.Clear();
            remaining = All;
        }

        public int Recommend()
        {
            if (remaining.Length == 1) return remaining[0];
            int best = 0, bestWorst = int.MaxValue, bestSquares = int.MaxValue;
            bool bestIsCandidate = false;
            var groups = new int[FieldCount + 1];
            foreach (int guess in All)
            {
                // Each possible feedback leaves one of these groups. Minimize the largest group first.
                Array.Clear(groups, 0, groups.Length);
                foreach (int candidate in remaining) groups[Differences[guess, candidate]]++;
                int worst = groups.Max(), squares = groups.Sum(size => size * size);
                bool isCandidate = Array.IndexOf(remaining, guess) >= 0;
                if (worst > bestWorst || (worst == bestWorst &&
                    (bestIsCandidate && !isCandidate || bestIsCandidate == isCandidate && squares >= bestSquares))) continue;
                best = guess;
                bestWorst = worst;
                bestSquares = squares;
                bestIsCandidate = isCandidate;
            }
            return best;
        }

        private static byte[,] BuildDifferences()
        {
            var result = new byte[CombinationCount, CombinationCount];
            for (int left = 0; left < CombinationCount; left++)
                for (int right = left + 1; right < CombinationCount; right++)
                {
                    byte count = 0;
                    for (int field = 0; field < FieldCount; field++)
                        if (OptionAt(left, field) != OptionAt(right, field)) count++;
                    result[left, right] = result[right, left] = count;
                }
            return result;
        }
    }
}

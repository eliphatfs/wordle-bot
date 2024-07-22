using System.Reflection;

namespace Info.Flandre.WordleBot
{
    public class Program
    {
        public static string? word = null;
        public static List<string> entries = new();
        public static List<(string word, float prob)> priorSet = new();
        public static List<string> wordList = new();
        public static void Main(string[] args)
        {
            Load();
            Console.Write("Enter the word here: ");
            word = EnterWord();
            if (!priorSet.Any(x => x.word == word))
            {
                Console.WriteLine(" -> NOT IN the word list! Exiting...");
                Console.ReadKey(true);
                return;
            }
            Console.WriteLine(" -> OK!");
            for (int i = 0; i < 6; i++)
            {
                var entry = EnterWord();
                Compute(entry, entries, word);
                Console.WriteLine();
                entries.Add(entry);
                if (entry == word)
                    break;
            }
            Console.WriteLine("Done.");
            Console.ReadKey(true);
            return;
        }

        public static void Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            var resourceName = asm.GetManifestResourceNames().Single(x => x.EndsWith("prior.tsv"));

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream == null)
                throw new ArgumentNullException(nameof(stream), "Fail to load word list.");
            using StreamReader reader = new(stream);
            while (reader.ReadLine() is string line)
            {
                var split = line.Trim().Split('\t');
                if (split.Length != 2)
                    throw new Exception($"Invalid word list: {split.Length} elements on line '{line}'.");
                if (split[0].Length != 5)
                    throw new Exception($"Invalid word on line '{line}'.");
                var word = split[0].ToUpperInvariant();
                var prob = float.Parse(split[1]);
                if (prob > 0)
                    priorSet.Add((word, prob));
                wordList.Add(word);
            }
        }

        public static string EnterWord()
        {
            string current = "";
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (current.Length > 0)
                    {
                        current = current[..^1];
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (key.Key == ConsoleKey.Enter)
                {
                    if (current.Length == 5)
                        return current;
                    continue;
                }
                if (current.Length == 5)
                    continue;
                var upperChar = char.ToUpperInvariant(key.KeyChar);
                if (word != null)
                {
                    if (word[current.Length] == upperChar)
                        Console.ForegroundColor = ConsoleColor.Green;
                    else if (word.Count(x => x == upperChar) > current.Count(x => x == upperChar))
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                }
                Console.Write(upperChar);
                Console.ResetColor();
                current += upperChar;
            }
        }

        public static string FormatScore(double x) => x >= 0 ? (99.499999 * x).ToString("0") : "-"; 

        public static List<(string word, float prob)> Filter(List<(string word, float prob)> candidates, string answer, string prev)
        {
            return candidates.Where(x =>
            {
                var word = x.word;
                for (int i = 0; i < 5; i++)
                {
                    if (prev[i] == answer[i])
                    {
                        if (prev[i] != word[i])
                            return false;
                    }
                    else if (prev[i] == word[i])
                        return false;
                    else
                    {
                        var yellowCount = Math.Min(prev.Count(x => x == prev[i]), answer.Count(x => x == prev[i]));
                        var wordCount = word.Count(x => x == prev[i]);
                        if (yellowCount > 0)
                        {
                            if (wordCount < yellowCount)
                                return false;
                        }
                        else
                        {
                            if (wordCount > 0)
                                return false;
                        }
                    }
                }
                return true;
            }).ToList();
        }

        public static float ScoreMap(float entropy, List<(string word, float prob)> remaining)
        {
            if (entropy <= 0) return 1;

            if (remaining.Count == 2)
            {
                var currentProbabilities = remaining.Select(d => d.prob).ToList();
                var p = currentProbabilities.Max() / currentProbabilities.Sum();
                return 2 - p;
            }

            var hardMode = false;
            var a = hardMode ? 2.5172585f : 2.4331083f;
            var b = hardMode ? 0.6538521f : 0.7114494f;
            var d = hardMode ? 0.4024986f : 0.3456398f;

            var estimatedGuesses = entropy <= 1 ? 1 + 0.5f * MathF.Pow(entropy, a) : 1.5f + d * MathF.Pow(entropy - 1, b);
            return estimatedGuesses;
        }

        public static (float exp, float luck) Expectation(List<(string word, float prob)> candidates, string answer, string entry)
        {
            var probNZ = candidates.Sum(x => x.prob);
            var exps = candidates.AsParallel().Select(x =>
            {
                var (word, prob) = x;
                var p = prob / probNZ;
                var left = Filter(candidates, word, entry);
                var leftNZ = left.Sum(x => x.prob);
                var entropy = left.Sum(x => -(x.prob / leftNZ) * MathF.Log2(x.prob / leftNZ));
                return ScoreMap(entropy, left);
            }).ToList();
            var exp = 0.0f;
            var luckTh = 0.0f;
            var luck = 0.0f;
            foreach (var (s, word) in exps.Zip(candidates, (x, y) => (x, y.word)))
            {
                if (word == answer)
                    luckTh = s;
            }
            foreach (var (subexp, word, prob) in exps.Zip(candidates, (x, y) => (x, y.word, y.prob)))
            {
                var p = prob / probNZ;
                exp += p * subexp;
                if (subexp > luckTh)
                    luck += p;
                else if (subexp == luckTh)
                    luck += p / 2;
            }
            var hitProb = candidates.Sum(x => x.word == entry ? x.prob : 0) / probNZ;
            return (exp - hitProb, luck);
        }

        public static void Compute(string entry, List<string> history, string answer)
        {
            var candidates = priorSet;
            foreach (var prev in history)
                candidates = Filter(candidates, answer, prev).ToList();
            var candidateList = priorSet.Select(x => x.word).ToHashSet();
            if (history.Count == 0)
                candidateList = new() { "TARSE", "XVIII" };
            // Dictionary<string, float> entropies = new();
            candidateList.Add(entry);
            Dictionary<string, (float exp, float luck)> expectation = new();
            foreach (var mayInput in candidateList)
            {
                // int maxGroup = 0;
                expectation[mayInput] = Expectation(candidates, answer, mayInput);
                // Console.WriteLine();
                // Console.Write($"{mayInput} {entropies[mayInput]} {maxGroup}");
            }
            var maxE = expectation.Values.Max().exp;
            var minE = expectation.Values.Min().exp;

            var left = Filter(candidates, answer, entry);

            var skill = 1 - (expectation[entry].exp - minE) / (maxE - minE);
            var luck = expectation[entry].luck;
            var best = expectation.First(x => x.Value.exp == minE).Key;

            Console.Write($" S {FormatScore(skill)} L {FormatScore(luck)} B {best}.");
            if (entry != answer)
                Console.Write($" {left.Count} left.");
        }
    }
}

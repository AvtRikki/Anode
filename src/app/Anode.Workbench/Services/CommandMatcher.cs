using Anode.Sdk;

namespace Anode.Workbench.Services;

/// <param name="Ranges">Character ranges of the title to colour as matched.</param>
public sealed record CommandMatch(CommandDescriptor Command, int Score, IReadOnlyList<(int Start, int Length)> Ranges);

/// <summary>
/// Command palette search. Every query word must occur in the title or scope; matches at the start of the title rank
/// first, then at word starts, then anywhere. Ties keep registration order.
/// </summary>
public static class CommandMatcher
{
    public static IReadOnlyList<CommandMatch> Match(IEnumerable<CommandDescriptor> commands, string query, int limit = 12)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<(CommandMatch Match, int Index)>();
        int index = 0;

        foreach (var command in commands)
        {
            if (Score(command, words) is { } match)
            {
                results.Add((match, index));
            }

            index++;
        }

        return [.. results
            .OrderByDescending(r => r.Match.Score)
            .ThenBy(r => r.Index)
            .Take(limit)
            .Select(r => r.Match)];
    }

    private static CommandMatch? Score(CommandDescriptor command, string[] words)
    {
        if (words.Length == 0)
        {
            return new CommandMatch(command, 0, []);
        }

        string title = command.Title;
        var ranges = new List<(int, int)>();
        int score = 0;

        foreach (string word in words)
        {
            int at = title.IndexOf(word, StringComparison.CurrentCultureIgnoreCase);
            if (at >= 0)
            {
                ranges.Add((at, word.Length));
                score += at == 0 ? 100 : char.IsWhiteSpace(title[at - 1]) || char.IsPunctuation(title[at - 1]) ? 60 : 30;
                continue;
            }

            if (command.Scope?.Contains(word, StringComparison.CurrentCultureIgnoreCase) == true
                || command.Id.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                score += 10;
                continue;
            }

            return null;
        }

        // Shorter titles are closer to what was typed.
        score -= Math.Min(title.Length, 60) / 6;
        return new CommandMatch(command, score, ranges);
    }
}

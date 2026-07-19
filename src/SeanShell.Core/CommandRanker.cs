namespace SeanShell.Core;

public static class CommandRanker
{
    private static readonly char[] WordSeparators = [' ', '-', '_', '.', '/', '\\', ':'];

    public static int Score(ShellCommand command, string query)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(query))
        {
            return command.Kind == ShellCommandKind.System ? 20 : 10;
        }

        var normalizedQuery = query.Trim();
        if (command.Title.Equals(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            return 10_000;
        }

        var total = 0;
        foreach (var token in normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var tokenScore = ScoreToken(command, token);
            if (tokenScore == 0)
            {
                return 0;
            }

            total += tokenScore;
        }

        return total;
    }

    private static int ScoreToken(ShellCommand command, string token)
    {
        if (command.Title.StartsWith(token, StringComparison.OrdinalIgnoreCase))
        {
            return 800;
        }

        if (command.Title.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(token, StringComparison.OrdinalIgnoreCase)))
        {
            return 650;
        }

        if (command.Title.Contains(token, StringComparison.OrdinalIgnoreCase))
        {
            return 500;
        }

        foreach (var keyword in command.Keywords)
        {
            if (keyword.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                return 450;
            }

            if (keyword.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            {
                return 350;
            }

            if (keyword.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return 250;
            }
        }

        return IsSubsequence(command.Title, token) ? 100 : 0;
    }

    private static bool IsSubsequence(string value, string query)
    {
        var queryIndex = 0;
        foreach (var character in value)
        {
            if (queryIndex < query.Length &&
                char.ToUpperInvariant(character) == char.ToUpperInvariant(query[queryIndex]))
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }
}

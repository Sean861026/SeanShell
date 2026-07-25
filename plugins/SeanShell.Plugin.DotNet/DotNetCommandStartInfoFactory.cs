using System.Diagnostics;

namespace SeanShell.Plugin.DotNet;

public static class DotNetCommandStartInfoFactory
{
    public static ProcessStartInfo Create(
        string workingDirectory,
        params string[] dotnetArguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(dotnetArguments);
        if (dotnetArguments.Count(static argument => !string.IsNullOrWhiteSpace(argument)) !=
            dotnetArguments.Length)
        {
            throw new ArgumentException(
                "dotnet arguments cannot be empty.",
                nameof(dotnetArguments));
        }

        var startInfo = new ProcessStartInfo("wt.exe")
        {
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(Path.GetFullPath(workingDirectory));
        startInfo.ArgumentList.Add("dotnet");
        foreach (var argument in dotnetArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static string Format(params string[] dotnetArguments)
    {
        ArgumentNullException.ThrowIfNull(dotnetArguments);
        return $"dotnet {string.Join(' ', dotnetArguments.Select(QuoteForDisplay))}";
    }

    private static string QuoteForDisplay(string argument) =>
        argument.Any(char.IsWhiteSpace) || Path.IsPathFullyQualified(argument)
            ? $"\"{argument}\""
            : argument;
}

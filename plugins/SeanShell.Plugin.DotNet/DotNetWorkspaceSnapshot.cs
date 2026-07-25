namespace SeanShell.Plugin.DotNet;

public sealed record DotNetWorkspaceSnapshot(
    string Path,
    string Name,
    string ProjectType,
    IReadOnlyList<string> TargetFrameworks,
    bool IsSolution)
{
    public string DirectoryPath => System.IO.Path.GetDirectoryName(Path) ?? Path;

    public string StatusText
    {
        get
        {
            if (IsSolution)
            {
                return ProjectType;
            }

            var frameworks = TargetFrameworks.Count == 0
                ? "target framework not specified"
                : string.Join(", ", TargetFrameworks);
            return $"{ProjectType} \u00B7 {frameworks}";
        }
    }
}

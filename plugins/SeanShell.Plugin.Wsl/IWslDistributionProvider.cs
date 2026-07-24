namespace SeanShell.Plugin.Wsl;

public interface IWslDistributionProvider
{
    ValueTask<IReadOnlyList<WslDistributionSnapshot>> GetDistributionsAsync(
        CancellationToken cancellationToken);
}

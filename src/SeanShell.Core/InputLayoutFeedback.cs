namespace SeanShell.Core;

/// <summary>A confirmed layout change for the original foreground application.</summary>
public sealed record InputLayoutFeedback(InputWindowIdentity Target, InputLayoutOption Layout);

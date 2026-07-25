using SeanShell.Plugin.DotNet;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class DotNetWorkspacePluginTests
{
    [TestMethod]
    public void DiscoveryFindsWorkspacesWithinBoundsAndSkipsBuildFolders()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var solution = Path.Combine(root, "Workspace.slnx");
            File.WriteAllText(solution, "<Solution />");
            var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Web"));
            var project = Path.Combine(projectDirectory.FullName, "Web.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");
            var ignoredDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Web", "obj"));
            File.WriteAllText(
                Path.Combine(ignoredDirectory.FullName, "Ignored.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var tooDeepDirectory = Directory.CreateDirectory(
                Path.Combine(root, "one", "two", "three"));
            File.WriteAllText(
                Path.Combine(tooDeepDirectory.FullName, "TooDeep.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            var workspaces = DotNetWorkspaceDiscovery.Discover(
                [root],
                maximumDepth: 2);

            CollectionAssert.AreEquivalent(
                new[] { Path.GetFullPath(solution), Path.GetFullPath(project) },
                workspaces.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InspectorClassifiesBlazorWebAssemblyAndReadsTargetFrameworks()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var project = Path.Combine(root, "Client.csproj");
            File.WriteAllText(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
                  <PropertyGroup>
                    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);

            var snapshot = DotNetWorkspaceInspector.Inspect(project);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual("Blazor WebAssembly", snapshot.ProjectType);
            Assert.IsFalse(snapshot.IsTestProject);
            Assert.IsTrue(snapshot.IsRunnable);
            CollectionAssert.AreEqual(
                new[] { "net9.0", "net10.0" },
                snapshot.TargetFrameworks.ToArray());
            Assert.AreEqual(
                "Blazor WebAssembly \u00B7 net9.0, net10.0",
                snapshot.StatusText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InspectorRecognizesRazorComponentsInWebProject()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var project = Path.Combine(root, "Server.csproj");
            File.WriteAllText(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "App.razor"), "<Router />");

            var snapshot = DotNetWorkspaceInspector.Inspect(project);

            Assert.IsNotNull(snapshot);
            Assert.AreEqual("ASP.NET Core / Blazor", snapshot.ProjectType);
            Assert.AreEqual("ASP.NET Core / Blazor \u00B7 net10.0", snapshot.StatusText);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void LaunchSettingsReaderKeepsOnlyLoopbackHttpUrls()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var properties = Directory.CreateDirectory(Path.Combine(root, "Properties"));
            File.WriteAllText(
                Path.Combine(properties.FullName, "launchSettings.json"),
                """
                {
                  // External and non-HTTP URLs must not become Launcher commands.
                  "profiles": {
                    "https": {
                      "applicationUrl": "https://localhost:7043;http://127.0.0.1:5043;https://example.com;file:///C:/temp/",
                    },
                    "duplicate": {
                      "applicationUrl": "https://localhost:7043/"
                    }
                  }
                }
                """);

            var urls = DotNetLaunchSettingsReader.ReadLocalApplicationUrls(root);

            CollectionAssert.AreEqual(
                new[]
                {
                    "https://localhost:7043",
                    "http://127.0.0.1:5043",
                },
                urls.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void InspectorRecognizesTestProjectAndDoesNotMarkItRunnable()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var project = Path.Combine(root, "Dashboard.Tests.csproj");
            File.WriteAllText(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsTestProject>true</IsTestProject>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Microsoft.NET.Test.Sdk" />
                  </ItemGroup>
                </Project>
                """);

            var snapshot = DotNetWorkspaceInspector.Inspect(project);

            Assert.IsNotNull(snapshot);
            Assert.IsTrue(snapshot.IsTestProject);
            Assert.IsFalse(snapshot.IsRunnable);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void DotNetStartInfoPassesExactArgumentsWithoutCommandShell()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var project = Path.Combine(root, "My Web App.csproj");

            var startInfo = DotNetCommandStartInfoFactory.Create(
                root,
                "run",
                "--project",
                project);

            Assert.AreEqual("wt.exe", startInfo.FileName);
            Assert.IsTrue(startInfo.UseShellExecute);
            CollectionAssert.AreEqual(
                new[]
                {
                    "-d",
                    Path.GetFullPath(root),
                    "dotnet",
                    "run",
                    "--project",
                    project,
                },
                startInfo.ArgumentList.ToArray());
            Assert.AreEqual(
                $"dotnet run --project \"{project}\"",
                DotNetCommandStartInfoFactory.Format("run", "--project", project));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task PluginReturnsCachedWorkspaceCommandsAndRefreshesOnRequest()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var project = Path.Combine(root, "Dashboard.csproj");
            File.WriteAllText(
                project,
                """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            await using var plugin = new DotNetWorkspacePlugin([root]);

            await plugin.InitializeAsync(CancellationToken.None);
            var initialCommands = await plugin.GetCommandsAsync(
                string.Empty,
                CancellationToken.None);

            Assert.HasCount(7, initialCommands);
            Assert.AreEqual(
                "ASP.NET Core \u00B7 net10.0 \u00B7 Open in default IDE",
                initialCommands.Single(command => command.Title == "Dashboard").Subtitle);
            Assert.IsTrue(initialCommands.Any(command =>
                command.Title == "Dashboard in VS Code"));
            Assert.IsTrue(initialCommands.Any(command =>
                command.Title == "Dashboard terminal"));
            Assert.AreEqual(
                $"dotnet build \"{project}\"",
                initialCommands.Single(command => command.Title == "Build Dashboard").Subtitle);
            Assert.AreEqual(
                $"dotnet run --project \"{project}\"",
                initialCommands.Single(command => command.Title == "Run Dashboard").Subtitle);
            Assert.AreEqual(
                $"dotnet watch run --project \"{project}\"",
                initialCommands.Single(command => command.Title == "Watch Dashboard").Subtitle);
            Assert.IsFalse(initialCommands.Any(command =>
                command.Title == "Test Dashboard"));

            File.WriteAllText(Path.Combine(root, "Dashboard.sln"), string.Empty);
            var refresh = initialCommands.Single(
                command => command.Title == "Refresh .NET workspaces");
            await refresh.ExecuteAsync(CancellationToken.None);
            var refreshedCommands = await plugin.GetCommandsAsync(
                string.Empty,
                CancellationToken.None);

            Assert.HasCount(12, refreshedCommands);
            Assert.IsTrue(refreshedCommands.Any(command =>
                command.Title == "Dashboard solution"));
            Assert.IsTrue(refreshedCommands.Any(command =>
                command.Title == "Build Dashboard solution"));
            Assert.IsTrue(refreshedCommands.Any(command =>
                command.Title == "Test Dashboard solution"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"SeanShell-dotnet-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}

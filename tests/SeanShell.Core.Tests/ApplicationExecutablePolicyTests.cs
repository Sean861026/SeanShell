using SeanShell.Core;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class ApplicationExecutablePolicyTests
{
    [DataRow(@"C:\Apps\Editor.exe", true)]
    [DataRow(@"C:\Apps\EDITOR.EXE", true)]
    [DataRow(@"C:\Apps\Editor.lnk", false)]
    [DataRow(@"\\server\share\Editor.exe", false)]
    [DataRow("Editor.exe", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    [TestMethod]
    public void IsSupportedLocalPathRejectsIndirectOrRemoteTargets(
        string? path,
        bool expected)
    {
        Assert.AreEqual(
            expected,
            ApplicationExecutablePolicy.IsSupportedLocalPath(path));
    }
}

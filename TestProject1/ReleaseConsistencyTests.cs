using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// 版番号と更新履歴の整合 (リリースの確認のうち、いつでも成り立つもの)。
///
/// <para>版番号は csproj の 4 か所 (Version・InformationalVersion・AssemblyVersion・FileVersion)、CHANGELOG の節、
/// CHANGELOG へ追記するスクリプト (tools/add-changelog.py) の目印にある。版を上げるときにどれかを取り残すと、
/// 画面の版・ファイルのプロパティの版・更新履歴が食い違う。リリースのときにだけ要る確認 (発行・[Unreleased] が空か) は
/// tools/release-check.ps1 が行う。</para>
/// </summary>
[TestClass]
public class ReleaseConsistencyTests
{
    private static string Csproj => TestSource.Read("Graphics_r1", "PileDesign.csproj");

    private static string Property(string name)
    {
        var m = Regex.Match(Csproj, $"<{name}>([^<]+)</{name}>");
        Assert.IsTrue(m.Success, $"(前提) csproj に {name} がありません");
        return m.Groups[1].Value.Trim();
    }

    [TestMethod]
    public void TheFourVersionsAgree()
    {
        string version = Property("Version");
        Assert.AreEqual(version, Property("InformationalVersion"), "InformationalVersion (画面の版) が Version と違います");
        string numeric = Regex.Match(version, @"^\d+\.\d+\.\d+").Value;
        Assert.AreEqual(numeric + ".0", Property("AssemblyVersion"));
        Assert.AreEqual(numeric + ".0", Property("FileVersion"), "FileVersion (ファイルのプロパティの版) が Version と違います");
    }

    /// <summary>CHANGELOG の [Unreleased] のすぐ下が今の版の節で、追記スクリプトの目印もその節を指す。</summary>
    [TestMethod]
    public void TheChangelogAndItsMarker_FollowTheVersion()
    {
        string version = Property("Version");
        var headers = File.ReadAllLines(Path.Combine(TestSource.Dir(), "CHANGELOG.md"))
            .Where(l => l.StartsWith("## [", System.StringComparison.Ordinal)).ToList();
        Assert.IsTrue(headers.Count >= 2, "(前提) CHANGELOG に節がありません");
        Assert.AreEqual("## [Unreleased]", headers[0].Trim(), "CHANGELOG の先頭の節が [Unreleased] ではありません");
        StringAssert.StartsWith(headers[1], $"## [{version}] — ", $"[Unreleased] の次の節が今の版 ({version}) ではありません");

        var marker = Regex.Match(File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "add-changelog.py")), @"MARKER\s*=\s*""\\n(## \[[^""]+)""");
        Assert.IsTrue(marker.Success, "(前提) add-changelog.py に MARKER がありません");
        Assert.AreEqual(headers[1].Trim(), marker.Groups[1].Value.Trim(), "add-changelog.py の目印が今の版の節を指していません");
    }

    /// <summary>リリースの確認の手順 (release-check.ps1) が、全体テスト・発行・版の確認を含む。</summary>
    [TestMethod]
    public void TheReleaseCheck_CoversTestsPublishAndVersions()
    {
        string script = File.ReadAllText(Path.Combine(TestSource.Dir(), "tools", "release-check.ps1"));
        StringAssert.Contains(script, "run-tests.ps1");
        StringAssert.Contains(script, "PublishProfile=FolderProfile");
        StringAssert.Contains(script, "[Unreleased]");
        StringAssert.Contains(script, "ProductVersion");
        var raw = File.ReadAllBytes(Path.Combine(TestSource.Dir(), "tools", "release-check.ps1"));
        Assert.IsTrue(raw.Length > 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF,
            "release-check.ps1 が BOM 付き UTF-8 ではありません (Windows PowerShell 5.1 で日本語が化けます)");
    }
}

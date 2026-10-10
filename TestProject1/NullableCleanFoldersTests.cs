using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TestProject1
{
    /// <summary>
    /// Null 許容の警告を片付け終えたフォルダーで、警告が増えていないこと。
    ///
    /// 本体は Null 許容の警告 (CS8600 など) を全体で「警告のまま」許している (PileDesign.csproj の WarningsNotAsErrors。
    /// 2026-10-10 時点で約 1,400 件)。片付けたフォルダーまで許したままだと、また増えても気づけない。
    /// .editorconfig でそのフォルダーだけ error にする方法は効かない
    /// (WarningsNotAsErrors が警告へ戻し、外すと TreatWarningsAsErrors が全体をエラーにする)。
    /// そこで本体のコンパイルが書き出す診断のファイル (ErrorLog、SARIF) を読んで確かめる。
    ///
    /// 片付けたら、そのフォルダーを <see cref="CleanFolders"/> に足すこと。
    /// </summary>
    [TestClass]
    public class NullableCleanFoldersTests
    {
        /// <summary>Null 許容の警告が 0 件のフォルダー (Graphics_r1 からの相対)。2026-10-10: Services・Output。</summary>
        private static readonly string[] CleanFolders = ["Services", "Output"];

        /// <summary>本体で警告のまま許している Null 許容の警告 (PileDesign.csproj の WarningsNotAsErrors と同じ)。</summary>
        private static readonly HashSet<string> NullableWarnings = new(StringComparer.Ordinal)
        {
            "CS8600", "CS8601", "CS8602", "CS8603", "CS8604", "CS8618", "CS8619", "CS8622", "CS8625",
        };

        [TestMethod]
        public void CleanFolders_HaveNoNullableWarnings()
        {
            string app = TestSource.Dir("Graphics_r1");
            string sarif = Path.Combine(app, "obj", "PileDesign.sarif");
            if (!File.Exists(sarif))
                Assert.Fail($"本体のコンパイルの診断のファイルがありません: {sarif}。本体をビルドしてから実行してください "
                    + "(PileDesign.csproj の ErrorLog が消えていないかも確かめてください)");

            // 古い診断で合格しないように、片付けたフォルダーのソースより新しいことを確かめる
            var sources = CleanFolders
                .SelectMany(f => Directory.GetFiles(Path.Combine(app, f), "*.cs", SearchOption.AllDirectories))
                .ToList();
            TestSource.AssertScanned(sources.Count, 30, "片付けたフォルダーのソース");
            DateTime newest = sources.Max(File.GetLastWriteTimeUtc);
            if (File.GetLastWriteTimeUtc(sarif) < newest)
                Assert.Fail("診断のファイルがソースより古いです。本体をビルドし直してから実行してください。"
                    + $" (診断 {File.GetLastWriteTime(sarif):G} / ソース {newest.ToLocalTime():G})");

            using var doc = JsonDocument.Parse(File.ReadAllBytes(sarif));
            var results = doc.RootElement.GetProperty("runs")[0].GetProperty("results");

            int nullableAnywhere = 0;
            var found = new List<string>();
            foreach (var r in results.EnumerateArray())
            {
                string rule = r.GetProperty("ruleId").GetString() ?? "";
                if (!NullableWarnings.Contains(rule)) continue;
                nullableAnywhere++;
                if (!r.TryGetProperty("locations", out var locs) || locs.GetArrayLength() == 0) continue;
                var loc = locs[0].GetProperty("physicalLocation");
                string uri = Uri.UnescapeDataString(loc.GetProperty("artifactLocation").GetProperty("uri").GetString() ?? "");
                string path = uri.Replace('\\', '/');
                foreach (var folder in CleanFolders)
                {
                    if (!path.Contains($"/Graphics_r1/{folder}/", StringComparison.OrdinalIgnoreCase)) continue;
                    int line = loc.TryGetProperty("region", out var region) ? region.GetProperty("startLine").GetInt32() : 0;
                    found.Add($"{rule} {folder}/{Path.GetFileName(path)}({line})");
                }
            }

            // 読み方が壊れて何も拾わなくなったら、空振りで合格してしまう。ほかのフォルダーにはまだ残っている
            TestSource.AssertScanned(nullableAnywhere, 1, "本体の Null 許容の警告");

            Assert.AreEqual(0, found.Count,
                "片付け終えたフォルダーに Null 許容の警告が増えました。null を取りうる型には ? を付ける、"
                + "確かめた値はパターン (is not { } x) で受けるなどで直してください:" + Environment.NewLine
                + string.Join(Environment.NewLine, found));
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TestProject1.LiteratureVerification
{
    /// <summary>
    /// 文献値カタログの照合と、検証の表の生成。
    ///
    /// 以前は verification.html の表が手書きで、文献値は画像の中、
    /// テストは 2 件だけが別ファイルに数値を持っていた。表とテストが別々に育つと
    /// 「テストは通るが表は古い」になるので、カタログ 1 つから両方を作る。
    ///
    /// 書き出す先は 4 つ。
    ///   verification.html         学会の指針・計算例の一覧 (先頭)
    ///   verification_methods.html 認定工法のカタログの一覧 (別ページ)
    ///   README.md                 両方の表
    ///   Help/verification_results.json  About が件数を読む
    ///
    /// 表の更新: 環境変数 <c>UPDATE_VERIFICATION_TABLE=1</c> を付けてこのクラスのテストを実行する。
    /// 付けずに実行したときに表が古ければ失敗する (更新し忘れの検出)。
    /// 更新後にもう一度ビルドしないと bin 側の Help には反映されない。
    /// </summary>
    [TestClass]
    public class VerificationTableTests
    {
        private static bool IsUpdateMode =>
            Environment.GetEnvironmentVariable("UPDATE_VERIFICATION_TABLE") == "1";

        private static string RepoRoot
        {
            get
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(VerificationTableTests).Assembly.Location)!);
                for (; dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "verification.html")))
                        return dir.FullName;
                }
                throw new DirectoryNotFoundException("リポジトリのルートが見つかりません (Graphics_r1/Help/verification.html)");
            }
        }

        private static string HtmlPath => Path.Combine(RepoRoot, "Graphics_r1", "Help", "verification.html");
        private static string MethodsHtmlPath => Path.Combine(RepoRoot, "Graphics_r1", "Help", "verification_methods.html");
        private static string ReadmePath => Path.Combine(RepoRoot, "README.md");
        private static string JsonPath => Path.Combine(RepoRoot, "Graphics_r1", "Help", "verification_results.json");

        private static readonly Lazy<List<LiteratureCheckResult>> _results =
            new(() => VerificationTableWriter.Evaluate(LiteratureCatalog.All));

        [TestMethod]
        public void Catalog_HasNoDuplicateEntries()
        {
            var dup = LiteratureCatalog.All
                .GroupBy(c => (c.Source, c.Section, c.Item))
                .Where(g => g.Count() > 1)
                .Select(g => $"{g.Key.Source} / {g.Key.Section} / {g.Key.Item}")
                .ToList();
            Assert.AreEqual(0, dup.Count, "同じ項目が二重に載っています:\n" + string.Join("\n", dup));
            Assert.IsTrue(LiteratureCatalog.All.Count >= 40, "カタログの件数が減っています (消した項目はありませんか)");
        }

        /// <summary>出典の区分が出典名と食い違わないこと (同じ出典が両方の表に散らばると件数が読めない)。</summary>
        [TestMethod]
        public void EachSource_BelongsToExactlyOneKind()
        {
            var mixed = LiteratureCatalog.All
                .GroupBy(c => c.Source)
                .Where(g => g.Select(c => c.SourceKind).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToList();
            Assert.AreEqual(0, mixed.Count, "出典の区分が混在しています: " + string.Join(", ", mixed));
        }

        [TestMethod]
        public void EveryCatalogEntry_IsWithinTheDocumentedTolerance()
        {
            var failed = _results.Value.Where(r => !r.Ok).ToList();
            Assert.AreEqual(0, failed.Count,
                "文献値と一致しない項目があります:\n" + string.Join("\n", failed.Select(Describe)));
        }

        /// <summary>
        /// 生成した表が、リポジトリに入っている表と同じであること。
        /// 違えば「カタログを変えたのに表を更新していない」か「計算値が変わった」のどちらか。
        /// </summary>
        [TestMethod]
        public void GeneratedTables_MatchTheCommittedFiles()
        {
            var results = _results.Value;
            string json = VerificationTableWriter.ToJson(results);
            string html = VerificationTableWriter.ToAcademicHtmlBlock(results);
            string methodsHtml = VerificationTableWriter.ToMethodsHtmlBlock(results);
            string md = VerificationTableWriter.ToMarkdownBlock(results);

            var targets = new (string Path, string Begin, string End, string Block, string Label)[]
            {
                (HtmlPath, VerificationTableWriter.HtmlBegin, VerificationTableWriter.HtmlEnd, html, "Graphics_r1/Help/verification.html"),
                (MethodsHtmlPath, VerificationTableWriter.MethodsHtmlBegin, VerificationTableWriter.MethodsHtmlEnd, methodsHtml, "Graphics_r1/Help/verification_methods.html"),
                (ReadmePath, VerificationTableWriter.MarkdownBegin, VerificationTableWriter.MarkdownEnd, md, "README.md"),
            };

            var stale = new List<string>();
            foreach (var t in targets)
            {
                Assert.IsTrue(File.Exists(t.Path), $"{t.Label} がありません");
                string? current = VerificationTableWriter.ExtractBetween(File.ReadAllText(t.Path, Encoding.UTF8), t.Begin, t.End);
                Assert.IsNotNull(current, $"{t.Label} に自動生成の印がありません: {t.Begin}");
                if (current != t.Block) stale.Add(t.Label);
            }

            string currentJson = File.Exists(JsonPath) ? File.ReadAllText(JsonPath, Encoding.UTF8).Replace("\r\n", "\n") : "";
            if (currentJson != json) stale.Add("Graphics_r1/Help/verification_results.json");

            if (IsUpdateMode)
            {
                foreach (var t in targets) WriteBetween(t.Path, t.Begin, t.End, t.Block);
                File.WriteAllText(JsonPath, json, new UTF8Encoding(false));
                return;
            }

            Assert.AreEqual(0, stale.Count,
                "検証の表がカタログの結果と違います: " + string.Join(", ", stale) + "\n" +
                "→ UPDATE_VERIFICATION_TABLE=1 を付けて VerificationTableTests を実行すると更新されます。\n" +
                "  (PowerShell) $env:UPDATE_VERIFICATION_TABLE=\"1\"; dotnet test TestProject1/TestProject1.csproj --filter FullyQualifiedName~VerificationTableTests");
        }

        private static void WriteBetween(string path, string begin, string end, string block)
        {
            byte[] raw = File.ReadAllBytes(path);
            bool bom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;
            string text = Encoding.UTF8.GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            string? replaced = VerificationTableWriter.ReplaceBetween(text, begin, end, block);
            Assert.IsNotNull(replaced, $"{Path.GetFileName(path)} に印が無く更新できません");
            File.WriteAllText(path, replaced, new UTF8Encoding(bom));
        }

        private static string Describe(LiteratureCheckResult r) =>
            $"  {r.Check.Source} / {r.Check.Section} / {r.Check.Item}: 文献 {LiteratureCheck.FormatValue(r.Check.Literature)} {r.Check.Unit}, " +
            $"プログラム {LiteratureCheck.FormatValue(r.Program)} {r.Check.Unit} (許容 {r.Check.ToleranceLabel})";
    }
}

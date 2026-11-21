using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// ヘルプの式の直下に置く「適用条件の小表」(外部レビュー #31 2026-11-21)。
    ///
    /// <para>式のほかに、適用する範囲・入力・採用値・適用外が本文に散らばっていて、式と一緒に読みにくかった。
    /// 行を 適用対象・入力・採用値・適用外・参照箇所 (・計算例) の順にそろえた表を式の直下に置く。
    /// 計算例の数値は、実装の式で計算した値と突き合わせる (式を直しても表が古いまま残らないように)。</para>
    /// </summary>
    [TestClass]
    public class FormulaConditionTableTests
    {
        private static readonly string[] Rows = ["適用対象", "入力", "採用値", "適用外", "参照箇所"];

        private static string Help() => TestSource.Read("Graphics_r1", "Help", "help.html");

        private static Dictionary<string, string> Tables()
        {
            var tables = Regex.Matches(Help(), "<table class=\"formula-conditions\" id=\"([^\"]+)\">(.*?)</table>", RegexOptions.Singleline)
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
            Assert.IsTrue(tables.Count >= 3, $"(前提) 適用条件の表を読めていない: {tables.Count}");
            return tables;
        }

        [TestMethod]
        public void EveryTableHasTheRowsInTheSameOrder()
        {
            Assert.AreEqual(Regex.Matches(Help(), "class=\"formula-conditions\"").Count, Tables().Count,
                "id の無い (または重複した) 適用条件の表があります");
            foreach (var (id, body) in Tables())
            {
                var heads = Regex.Matches(body, "<th>([^<]+)</th>").Select(m => m.Groups[1].Value).ToList();
                CollectionAssert.AreEqual(Rows, heads.Take(Rows.Length).ToList(), $"{id}: 行の並びが 適用対象・入力・採用値・適用外・参照箇所 ではありません");
                Assert.IsTrue(heads.Count == Rows.Length || (heads.Count == Rows.Length + 1 && heads[^1] == "計算例"),
                    $"{id}: 決まった行のあとに置けるのは「計算例」だけです: {string.Join(", ", heads)}");
            }
        }

        [TestMethod]
        public void TheLinksResolve()
        {
            string help = Regex.Replace(Help(), "<!--.*?-->", "", RegexOptions.Singleline);
            var ids = new HashSet<string>(Regex.Matches(help, "\\bid=\"([^\"]+)\"").Select(m => m.Groups[1].Value));
            var broken = Tables().Values
                .SelectMany(b => Regex.Matches(b, "href=\"#([^\"]+)\"").Select(m => m.Groups[1].Value))
                .Where(t => !ids.Contains(t)).Distinct().ToList();
            Assert.AreEqual(0, broken.Count, "適用条件の表のリンク先が見つかりません: " + string.Join(", ", broken));
        }

        private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>鋼管杭の柱座屈・局部座屈の計算例が、実装の式の値と一致する。</summary>
        [TestMethod]
        public void TheSteelPipeExamplesMatchTheImplementation()
        {
            var tables = Tables();
            string buckling = tables["cond-steel-pipe-buckling"];
            string local = tables["cond-steel-pipe-local-buckling"];

            static SteelPipeSection Pipe(double t, double lk) => new(600, t, 235, 1.0, 0.0, 0.0, 205000.0, lk);

            // 局部座屈 sfc1
            foreach (double t in new[] { 24.0, 12.0, 6.0 })
                StringAssert.Contains(local, F1(Pipe(t, 0).Sfc1), $"t = {t} の sfc1 が表と違います");
            Assert.AreEqual(235.0 / 1.5, Pipe(24, 0).Sfc1, 1e-9, "D/t = 25 で低減なしになっていない");

            // 柱座屈 sfc2: 0 m・5 m・10 m
            StringAssert.Contains(buckling, $"${{}}_sf_{{c1}} = {F1(Pipe(12, 0).Sfc1)}$");
            StringAssert.Contains(buckling, $"F/1.5 = {F1(Pipe(12, 0).Sfc2)}");
            StringAssert.Contains(buckling, $"{{}}_sf_{{c2}} = {F1(Pipe(12, 5).Sfc2)}$ → まだ");
            StringAssert.Contains(buckling, $"{{}}_sf_{{c2}} = {F1(Pipe(12, 10).Sfc2)}$ → <strong>柱座屈系");
            Assert.IsTrue(Pipe(12, 5).Sfc2 > Pipe(12, 5).Sfc1 && Pipe(12, 10).Sfc2 < Pipe(12, 10).Sfc1, "(前提) 5 m と 10 m で支配が入れ替わる");

            // 「約 9 m が境」: 8.5 m では局部座屈系、9.5 m では柱座屈系
            Assert.IsTrue(Pipe(12, 8.5).Sfc2 > Pipe(12, 8.5).Sfc1 && Pipe(12, 9.5).Sfc2 < Pipe(12, 9.5).Sfc1, "境が約 9 m ではありません");
        }
    }
}

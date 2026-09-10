using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 諸元を変えたら、耐力曲線のキャッシュが<b>捨てられる</b>こと。
    ///
    /// N-M / N-Q / ひび割れ / 鉄筋降伏の曲線は断面ごとの入れ物に貯めており、
    /// M-φ と違って<b>鍵を持たない</b>。ただの nullable なフィールドで、
    /// セッターが <c>InvalidateAllCaches()</c> を呼んで捨てる仕組みになっている
    /// (呼び出しは 30 か所)。
    ///
    /// つまり<b>捨て忘れがそのまま古い曲線になる</b>。鍵のように「入れ忘れ」を
    /// 目で追える形ではなく、セッターを 1 つ足して呼び忘れれば静かに漏れる。
    ///
    /// 判定はこれだけ。<b>諸元を変えて曲線が変わるなら、変えた直後に読んだ曲線も
    /// 変わっていなければならない。</b>
    ///
    /// 比べ方が要点で、「変えた直後に読んだ値」と「捨ててから読んだ値」を比べる。
    /// 後者が本来の値なので、食い違えば捨て忘れ。
    /// </summary>
    [TestClass]
    public class CurveCacheInvalidationTests
    {
        /// <summary>
        /// 捨てられなくてよい諸元と、その理由。<b>増やすときは理由を書くこと。</b>
        ///
        /// ここに挙げるのは「セッターが捨てていないが、<b>到達できない</b>」もの。
        /// 免除そのものが手書きの一覧なので、前提が崩れていないかを
        /// <see cref="ExemptedProperties_AreStillUnreachable"/> が検査する。
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
        {
            // 既製杭の断面を決める値。書き込むのは製品カタログを当てる経路
            // (SetSelectedPrecastPileByName → RecalculateSelectedPrecastPile) だけで、
            // その経路は最後に InvalidateAllCaches() を呼ぶ。画面にも束縛されておらず、
            // PileSection.cs の外から書かれてもいないので、古い曲線には到達できない。
            ["PileDiameter"] = "カタログ経路のみが書く (最後に破棄する)",
            ["Prestress"] = "カタログ経路のみが書く",
            ["TendonAp"] = "カタログ経路のみが書く",
            ["TendonDp"] = "カタログ経路のみが書く",
            ["TendonSigmaPy"] = "カタログ経路のみが書く",
            ["TendonSigmaPu"] = "カタログ経路のみが書く",
        };

        /// <summary>
        /// 免除した諸元が、いまも「到達できない」ままであること。
        ///
        /// 免除の根拠は<b>書き込む道がカタログ経路しかない</b>こと。画面に束縛されたり、
        /// 別のクラスから代入されたりすれば前提が崩れ、免除は誤りになる。
        /// そこを見張らないと、免除が「調べずに黙らせる仕掛け」になってしまう。
        /// </summary>
        [TestMethod]
        public void ExemptedProperties_AreStillUnreachable()
        {
            var offenders = new List<string>();

            foreach (var name in Exempt.Keys)
            {
                // 画面に束縛されていないこと
                foreach (var xaml in System.IO.Directory.GetFiles(
                    System.IO.Path.Combine(TestSource.Root(), "Graphics_r1", "Views"), "*.xaml"))
                {
                    string text = System.IO.File.ReadAllText(xaml);
                    if (text.Contains($"PileSection.{name}", StringComparison.Ordinal))
                        offenders.Add($"{name}: {System.IO.Path.GetFileName(xaml)} で画面に束縛されています");
                }

                // PileSection.cs の外から代入されていないこと
                foreach (var cs in System.IO.Directory.GetFiles(
                    System.IO.Path.Combine(TestSource.Root(), "Graphics_r1"), "*.cs",
                    System.IO.SearchOption.AllDirectories))
                {
                    if (cs.Contains(System.IO.Path.DirectorySeparatorChar + "obj"
                                    + System.IO.Path.DirectorySeparatorChar)) continue;
                    if (System.IO.Path.GetFileName(cs) == "PileSection.cs") continue;

                    string text = System.IO.File.ReadAllText(cs);
                    if (System.Text.RegularExpressions.Regex.IsMatch(
                            text, @"\.\s*" + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*=(?!=)"))
                        offenders.Add($"{name}: {System.IO.Path.GetFileName(cs)} から代入されています");
                }
            }

            TestSource.AssertScanned(Exempt.Count, 1, "免除した諸元");

            Assert.AreEqual(0, offenders.Count,
                "免除の前提が崩れています。書き込む道が増えたので、"
                + "セッターで InvalidateAllCaches() を呼ぶか、免除の理由を書き直してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]        // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]    // 既製杭 (SC + PRC)
        [DataRow("Example3_5", "PileExample3_5")]    // 鋼管杭
        public void ChangingAnyProperty_DropsTheCurveCache(string groundName, string pileName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            var sections = model.PileBodies
                .SelectMany(pb => pb.PileBodySegments)
                .Select(seg => seg.PileSection)
                .Where(s => s != null)
                .GroupBy(s => (s!.PileBodyType, s.PileSectionType))
                .Select(g => g.First()!)
                .ToList();

            TestSource.AssertScanned(sections.Count, 1, $"{groundName} の断面タイプ");

            var offenders = new List<string>();
            int checkedProps = 0;

            foreach (var section in sections)
            {
                string what = $"{section.PileBodyType}/{section.PileSectionType}";

                // 先に読んでキャッシュを埋める
                string? filled = Curve(section);
                if (filled == null) continue;   // この断面では曲線が作れない

                foreach (var p in Perturbable())
                {
                    object? before;
                    try { before = p.GetValue(section); } catch { continue; }

                    object? candidate = Candidate(p, before);
                    if (candidate == null) continue;

                    try { p.SetValue(section, candidate); } catch { continue; }

                    object? after;
                    try { after = p.GetValue(section); } catch { after = before; }
                    if (Equals(before, after)) { Restore(section, p, before); continue; }

                    // 派生値は対象外。直に書いても再計算で戻るので到達できない状態
                    try { section.RecalculatePileDia(); } catch { }
                    object? afterRecalc;
                    try { afterRecalc = p.GetValue(section); } catch { afterRecalc = after; }
                    if (!Equals(after, afterRecalc)) { Restore(section, p, before); continue; }

                    // 変えた直後に読んだ曲線 (捨て忘れていれば古いまま)
                    string? asRead = Curve(section);

                    // 捨ててから読んだ曲線 (これが本来の値)
                    Invalidate(section);
                    string? fresh = Curve(section);

                    if (fresh != null && fresh != filled)
                    {
                        // この諸元は曲線に効く。ならば変えた直後も本来の値でなければならない
                        checkedProps++;
                        if (asRead != fresh && !Exempt.ContainsKey(p.Name))
                            offenders.Add($"{what}: {p.Name} ({before} → {after})");
                    }

                    Restore(section, p, before);
                    Invalidate(section);
                    filled = Curve(section) ?? filled;
                }
            }

            TestSource.AssertScanned(checkedProps, 3, $"{groundName} で曲線が動いた諸元");

            Assert.AreEqual(0, offenders.Count,
                "諸元を変えても耐力曲線のキャッシュが捨てられない箇所があります。"
                + "古い曲線がそのまま使われます (値は正しく見えるので気づきにくい)。"
                + "セッターで InvalidateAllCaches() を呼んでください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(20)));
        }

        private static IEnumerable<PropertyInfo> Perturbable()
            => typeof(PileSection)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(int))
                .Where(p => p.Name is not (nameof(PileSection.PileBodyType)
                                        or nameof(PileSection.PileSectionType)))
                .OrderBy(p => p.Name, StringComparer.Ordinal);

        private static object? Candidate(PropertyInfo p, object? before)
        {
            if (p.PropertyType == typeof(double))
            {
                double v = before is double d ? d : 0.0;
                return Math.Abs(v) > 1e-9 ? v * 1.1 : 1.0;
            }
            if (p.PropertyType == typeof(int)) return (before is int i ? i : 0) + 1;
            return null;
        }

        private static void Restore(PileSection section, PropertyInfo p, object? before)
        {
            try { p.SetValue(section, before); } catch { }
        }

        private static void Invalidate(PileSection section)
        {
            var m = typeof(PileSection).GetMethod("InvalidateAllCaches",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "InvalidateAllCaches が見つかりません (名前が変わった?)");
            m!.Invoke(section, null);
        }

        /// <summary>
        /// 安全限界の N-M 曲線の指紋。断面が対応していなければ null。
        ///
        /// 読むのは <c>UnfactoredUltimateNM</c>（<b>Raw なし</b>）。Raw は毎回計算し直すので、
        /// そちらを読むとキャッシュを経由せず、捨て忘れを検出できない
        /// (最初に Raw で書いたため、破棄を外しても網が鳴らなかった)。
        /// </summary>
        private static string? Curve(PileSection section)
        {
            try
            {
                var (ns, ms) = section.UnfactoredUltimateNM;
                if (ns == null || ms == null || ns.Count == 0) return null;
                return string.Join(",", ns.Select(x => x.ToString("G10", CultureInfo.InvariantCulture)))
                    + "|"
                    + string.Join(",", ms.Select(x => x.ToString("G10", CultureInfo.InvariantCulture)));
            }
            catch { return null; }
        }
    }
}

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
    /// M-φ キャッシュの鍵に、曲線に効く諸元が<b>残らず</b>入っていること。
    ///
    /// 鍵は断面タイプごとに手で組んだ文字列 (<c>PileSection.GetMPhiCacheKey</c>)。
    /// 曲線に効く諸元が鍵に入っていないと、その諸元を変えても<b>古い曲線が返る</b>。
    /// 値は正しく見えるので気づきにくい。実際に鋼管杭系の OTHER キーで起きている。
    ///
    /// <see cref="MphiCacheKeyTests"/> は杭種ごとに「この諸元を変えたら鍵が変わる」を
    /// 手で並べて見ている。それも要るが、<b>並べた一覧自体が取り残される</b>。
    /// 諸元を足して鍵に入れ忘れ、しかもテストにも書き忘れれば、両方とも静かに漏れる。
    ///
    /// そこで一覧を持たない形にする。判定はこれだけ。
    ///
    /// <b>ある諸元を変えて曲線が変わったなら、鍵も変わらなければならない。</b>
    ///
    /// 逆は要らない。鍵に余分なものが入っていてもキャッシュが効きにくくなるだけで、
    /// 間違った曲線は返らない。
    ///
    /// 曲線を取るときは必ずキャッシュを捨てる。捨てないと、まさに調べたい不具合
    /// (古い鍵で古い曲線が返る) のせいで変化が見えなくなる。
    /// </summary>
    [TestClass]
    public class MphiCacheKeyCompletenessTests
    {
        private const double AxialN = 1000.0;

        /// <summary>
        /// 鍵に入っていなくてよい諸元。<b>増やすときは理由を書くこと。</b>
        /// </summary>
        private static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
        {
            // いまのところ無し。
        };

        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]        // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]    // 既製杭 (SC + PRC)
        [DataRow("Example3_5", "PileExample3_5")]    // 鋼管杭
        [DataRow("Example3_8_1", "PileExample3_8")]  // 場所打ち鋼管コンクリート杭
        public void EveryPropertyThatChangesTheCurve_AlsoChangesTheKey(string groundName, string pileName)
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

                string? baseCurve = CurveSignature(section);
                if (baseCurve == null) continue;   // この断面では曲線が作れない
                string baseKey = Key(section);

                foreach (var p in Perturbable())
                {
                    object? before;
                    try { before = p.GetValue(section); } catch { continue; }

                    object? candidate = Candidate(p, before);
                    if (candidate == null) continue;

                    try { p.SetValue(section, candidate); } catch { continue; }

                    object? after;
                    try { after = p.GetValue(section); } catch { after = before; }

                    // クランプや無視で動かなかったものは検査対象にならない
                    if (Equals(before, after))
                    {
                        Restore(section, p, before);
                        continue;
                    }

                    // 派生値は検査対象にしない。直に書いても再計算で戻るので、
                    // 利用者が到達できる状態ではない (CorrodedPipeTs = PipeTs - CorrosionDepth など。
                    // 元になる 2 つは鍵に入っている)。
                    // 判定は「再計算したら値が戻るか」— 一覧を持たずに済む。
                    try { section.RecalculatePileDia(); } catch { }
                    object? afterRecalc;
                    try { afterRecalc = p.GetValue(section); } catch { afterRecalc = after; }
                    if (!Equals(after, afterRecalc))
                    {
                        Restore(section, p, before);
                        continue;
                    }

                    string? curve = CurveSignature(section);
                    string key = Key(section);

                    if (curve != null && curve != baseCurve)
                    {
                        checkedProps++;
                        if (key == baseKey && !Exempt.ContainsKey(p.Name))
                            offenders.Add($"{what}: {p.Name} ({before} → {after})");
                    }

                    Restore(section, p, before);
                }
            }

            TestSource.AssertScanned(checkedProps, 5, $"{groundName} で曲線が動いた諸元");

            Assert.AreEqual(0, offenders.Count,
                "曲線が変わるのにキャッシュの鍵が変わらない諸元があります。"
                + "その諸元を変えても古い曲線が返ります (値は正しく見えるので気づきにくい)。"
                + "GetMPhiCacheKey に足してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(20)));
        }

        /// <summary>揺らす候補。書き込める double / int / string に限る。</summary>
        private static IEnumerable<PropertyInfo> Perturbable()
            => typeof(PileSection)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.SetMethod?.IsPublic == true)
                .Where(p => p.PropertyType == typeof(double)
                         || p.PropertyType == typeof(int)
                         || p.PropertyType == typeof(string))
                // 断面タイプそのものを変えると別の断面の話になる
                .Where(p => p.Name is not (nameof(PileSection.PileBodyType)
                                        or nameof(PileSection.PileSectionType)))
                .OrderBy(p => p.Name, StringComparer.Ordinal);

        /// <summary>
        /// 揺らす値。既定値と違い、かつ物理的に極端でない値にする。
        /// 文字列は<b>触らない</b> — 規格名や呼び径を壊すと別の断面の話になる。
        /// </summary>
        private static object? Candidate(PropertyInfo p, object? before)
        {
            if (p.PropertyType == typeof(double))
            {
                double v = before is double d ? d : 0.0;
                // 1 割ほど増やす。0 のときは小さな正の値
                return Math.Abs(v) > 1e-9 ? v * 1.1 : 1.0;
            }
            if (p.PropertyType == typeof(int))
            {
                int v = before is int i ? i : 0;
                return v + 1;
            }
            return null;   // string は触らない
        }

        private static void Restore(PileSection section, PropertyInfo p, object? before)
        {
            try { p.SetValue(section, before); } catch { }
        }

        private static string Key(PileSection section)
        {
            var m = typeof(PileSection).GetMethod("GetMPhiCacheKey",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "GetMPhiCacheKey が見つかりません (名前が変わった?)");
            return (string)m!.Invoke(section, [AxialN])!;
        }

        /// <summary>
        /// 曲線の指紋。<b>必ずキャッシュを捨ててから</b>作る。
        /// 捨てないと、調べたい不具合そのもののせいで変化が見えない。
        /// </summary>
        private static string? CurveSignature(PileSection section)
        {
            try
            {
                PileSection.ClearMphiCache();
                var (phis, moments) = section.GetMPhiRelationship(AxialN);
                if (phis == null || moments == null || phis.Count == 0) return null;

                return string.Join(",", phis.Select(x => x.ToString("G12", CultureInfo.InvariantCulture)))
                    + "|"
                    + string.Join(",", moments.Select(x => x.ToString("G12", CultureInfo.InvariantCulture)));
            }
            catch
            {
                return null;   // 揺らした結果、曲線が作れない状態になった
            }
        }
    }
}

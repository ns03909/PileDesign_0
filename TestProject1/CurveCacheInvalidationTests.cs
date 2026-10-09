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
    /// セッターが <c>InvalidateAllCaches()</c> を呼んで捨てる仕組みになっている。
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
        // 以前はここに「捨てられなくてよい諸元」の免除一覧があった
        // (PileDiameter・Prestress・TendonAp/Dp/SigmaPy/SigmaPu)。
        // 根拠は「書き込むのはカタログ経路だけで、その経路は最後に InvalidateAllCaches() を呼ぶ」
        // だったが、<b>杭断面ウィンドウは RecalculateSelectedPrecastPile() を直に呼び</b>、
        // そちらは最後に捨てていなかった。寸法・Fc が同じで σe と Ap だけが違う製品
        // (PHC-600-標準-85-B → 85-C) に切り替えると、前の製品の N-M・N-Q が残っていた。
        // 免除の検査は「画面に束縛されていない・他のクラスから代入されていない」しか見ておらず、
        // 同じクラスの中の経路の取りこぼしは捕まえられなかった。
        // セッターで捨てるようにしたので免除は不要になった。製品の切り替えそのものは
        // SwitchingPrecastProduct_* が見張る。

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
                        if (asRead != fresh)
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

        /// <summary>
        /// 寸法・Fc・Ec・dp が同じで σe と Ap だけが違う製品に切り替えたとき、
        /// 耐力曲線 (N-M・N-Q) が新しい製品のものになること。
        ///
        /// 杭断面ウィンドウの製品の ComboBox は <c>SelectedPrecastPile.Name</c> を書いて
        /// <c>RecalculateSelectedPrecastPile()</c> を直に呼ぶ。以前はこの経路が曲線を
        /// 捨てておらず、σe と Ap のセッターも捨てていなかったので、B 種の曲線が残っていた。
        /// 曲線を先に読んでキャッシュを埋めてから切り替えるのが要点。
        /// </summary>
        [DataTestMethod]
        [DataRow("PHC-600-標準-85-B", "PHC-600-標準-85-C")]
        [DataRow("PHC-600-標準-85-C", "PHC-600-標準-85-B")]
        public void SwitchingPrecastProduct_ViaWindowPath_RefreshesCurves(string from, string to)
        {
            var section = Phc(from);
            string nmBefore = Fingerprint(section.UnfactoredUltimateNM);
            string nqBefore = Fingerprint(section.UnfactoredUltimateNQ);
            string nmL1Before = Fingerprint(section.FactoredDamageNMLevel1);

            // 画面と同じ書き方 (ComboBox_PileSectionSelectionChanged)
            section.SelectedPrecastPile.Name = to;
            section.RecalculateSelectedPrecastPile();
            Assert.AreEqual(to, section.SelectedPrecastPile.Name, "製品が切り替わっていません");

            AssertMatchesFresh(section, to, nmBefore, nqBefore, nmL1Before);
        }

        /// <summary>名前で当てる経路 (SetSelectedPrecastPileByName) でも同じであること。</summary>
        [TestMethod]
        public void SwitchingPrecastProduct_ByName_RefreshesCurves()
        {
            var section = Phc("PHC-600-標準-85-B");
            string nmBefore = Fingerprint(section.UnfactoredUltimateNM);
            string nqBefore = Fingerprint(section.UnfactoredUltimateNQ);
            string nmL1Before = Fingerprint(section.FactoredDamageNMLevel1);

            section.SetSelectedPrecastPileByName("PHC-600-標準-85-C");

            AssertMatchesFresh(section, "PHC-600-標準-85-C", nmBefore, nqBefore, nmL1Before);
        }

        /// <summary>
        /// 複製 (DeepCopy) が元の断面の曲線を持ち越さないこと。
        ///
        /// 以前の DeepCopy はキャッシュを 1 つずつ null にしており、レベル1 の損傷 N-M と
        /// N-Q 6 本が漏れていた。複製側で σe を変えても、それらは元の曲線のままだった。
        /// </summary>
        [TestMethod]
        public void DeepCopy_DoesNotCarryOverCurves()
        {
            var original = Phc("PHC-600-標準-85-B");
            string nqBefore = Fingerprint(original.UnfactoredUltimateNQ);
            string nmL1Before = Fingerprint(original.FactoredDamageNMLevel1);

            var copy = original.DeepCopy();

            // 複製の曲線キャッシュは空であること (フィールドを直に見る)
            var cacheFields = typeof(PileSection)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.Name.EndsWith("Cache", StringComparison.Ordinal)
                            && Nullable.GetUnderlyingType(f.FieldType) is { IsValueType: true } u
                            && u.IsGenericType
                            && u.GetGenericArguments().All(a => a == typeof(List<double>)))
                .ToList();
            TestSource.AssertScanned(cacheFields.Count, 14, "曲線キャッシュのフィールド");
            var leftovers = cacheFields.Where(f => f.GetValue(copy) != null).Select(f => f.Name).ToList();
            Assert.AreEqual(0, leftovers.Count,
                "複製が元の断面の曲線を持ち越しています: " + string.Join(", ", leftovers));

            // 曲線に効く値を変えれば、複製の曲線は変わる
            copy.Prestress *= 1.25;
            Assert.AreNotEqual(nqBefore, Fingerprint(copy.UnfactoredUltimateNQ), "N-Q が元のまま");
            Assert.AreNotEqual(nmL1Before, Fingerprint(copy.FactoredDamageNMLevel1), "レベル1 の N-M が元のまま");
        }

        private static PileSection Phc(string product)
        {
            var s = new PileSection
            {
                PileBodyType = PileDesign.Constants.PileTypeNames.PrecastConcrete,
                PileSectionType = PileDesign.Constants.PileTypeNames.Phc,
            };
            s.SelectedPrecastPile.Name = product;
            s.RecalculateSelectedPrecastPile();
            Assert.AreEqual(product, s.SelectedPrecastPile.Name, $"{product} がライブラリにありません");
            return s;
        }

        /// <summary>切り替え後の曲線が、同じ製品を新しく組んだ断面の曲線と一致し、かつ前の製品と違うこと。</summary>
        private static void AssertMatchesFresh(PileSection section, string product,
            string nmBefore, string nqBefore, string nmL1Before)
        {
            var fresh = Phc(product);

            string nm = Fingerprint(section.UnfactoredUltimateNM);
            string nq = Fingerprint(section.UnfactoredUltimateNQ);
            string nmL1 = Fingerprint(section.FactoredDamageNMLevel1);

            // 前提: σe と Ap が違うので曲線は変わるはず (変わらなければ検査が空振りする)
            Assert.AreNotEqual(nmBefore, Fingerprint(fresh.UnfactoredUltimateNM), "前提: 2 製品の N-M が同じです");
            Assert.AreNotEqual(nqBefore, Fingerprint(fresh.UnfactoredUltimateNQ), "前提: 2 製品の N-Q が同じです");

            Assert.AreEqual(Fingerprint(fresh.UnfactoredUltimateNM), nm, "N-M が前の製品のまま残っています");
            Assert.AreEqual(Fingerprint(fresh.UnfactoredUltimateNQ), nq, "N-Q が前の製品のまま残っています");
            Assert.AreEqual(Fingerprint(fresh.FactoredDamageNMLevel1), nmL1, "レベル1 の N-M が前の製品のまま残っています");
            Assert.AreNotEqual(nmL1Before, nmL1, "レベル1 の N-M が変わっていません");
        }

        private static string Fingerprint((List<double> A, List<double> B) curve)
        {
            Assert.IsNotNull(curve.A, "曲線が作れません");
            Assert.IsTrue(curve.A.Count > 0, "曲線が空です");
            return string.Join(",", curve.A.Select(x => x.ToString("G10", CultureInfo.InvariantCulture)))
                + "|"
                + string.Join(",", curve.B.Select(x => x.ToString("G10", CultureInfo.InvariantCulture)));
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

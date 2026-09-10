using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 沈下ウィンドウの「キャンセル」で、杭体入力が<b>残らず</b>元に戻ること。
    ///
    /// このウィンドウは地盤杭セット (<c>SoilPiles</c>) は <c>DeepCopy</c> を編集するので
    /// キャンセルは捨てるだけでよいが、<b>杭体入力 (PileBody) は
    /// <c>InputModel.PileBodies</c> の実体をそのまま編集する</b>。
    /// 以前はキャンセルが何も戻していなかったため、
    /// 先端径・沈下用先端径・α・N・杭体記号・先端非排水率をいじってキャンセルしても
    /// そのまま残り、支持力と沈下の計算が変わっていた。
    ///
    /// 戻す項目を手で並べる形にすると、沈下側の入力が増えるたびに取り残される
    /// (杭断面ウィンドウで実際に起きた。<see cref="PileSectionRestoreTests"/>)。
    /// そこでまるごと写す <c>PileBodyInput.RestoreScalarsFrom</c> にし、
    /// その完全性をここで見張る。
    ///
    /// <para><b>3 本で役割を分けている。</b>
    /// <see cref="Cancelling_RestoresEveryScalar"/> は写しの完全性、
    /// <see cref="TheCancel_RestoresThePileBodies"/> はキャンセルがそれを呼んでいること、
    /// <see cref="TheWindow_OnlyWritesScalarsToTheLiveModel"/> は
    /// <b>写しの届く範囲に画面の書き込みが収まっていること</b>を見る。
    /// 3 本目がないと、あとから参照型 (セグメント等) を触るようになったときに
    /// 静かに戻らなくなる。</para>
    /// </summary>
    [TestClass]
    public class PileBodyScalarRestoreTests
    {
        /// <summary>沈下ウィンドウが実体へ書く項目。ここが戻らないと結果が変わる。</summary>
        private static readonly string[] MustBeRestored =
        [
            nameof(PileBodyInput.PileBodyRef),
            nameof(PileBodyInput.PileToeDia),
            nameof(PileBodyInput.SettlePileToeDia),
            nameof(PileBodyInput.SettleAlpha),
            nameof(PileBodyInput.SettleN),
            nameof(PileBodyInput.TipNonPermability),
        ];

        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]        // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]    // 既製杭
        [DataRow("Example3_5", "PileExample3_5")]    // 鋼管杭
        public void Cancelling_RestoresEveryScalar(string groundName, string pileName)
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel(groundName, pileName);
            if (model == null) { Assert.Inconclusive($"{groundName}+{pileName}: {error}"); return; }

            TestSource.AssertScanned(model.PileBodies?.Count ?? 0, 1, $"{groundName} の杭体");

            var offenders = new List<string>();
            int moved = 0;

            foreach (var body in model.PileBodies!)
            {
                if (body == null) continue;

                // ウィンドウを開いたときの控え
                var saved = body.DeepCopy();
                Assert.IsNotNull(saved, "DeepCopy が null を返しました");

                var before = Snapshot(body);

                // 参照型は写さない約束なので、実体が入れ替わっていないことも見る
                var refsBefore = ReferenceMembers(body);

                // 画面でいじった状態を作る
                moved += PerturbOwnScalars(body);

                // キャンセル
                Restore(body, saved!);

                var after = Snapshot(body);
                foreach (var kv in before)
                {
                    if (!after.TryGetValue(kv.Key, out var now)) continue;
                    if (!Equals(kv.Value, now))
                        offenders.Add($"{kv.Key} 元 {kv.Value} → キャンセル後 {now}");
                }

                var refsAfter = ReferenceMembers(body);
                foreach (var kv in refsBefore)
                {
                    if (!refsAfter.TryGetValue(kv.Key, out var now)) continue;
                    if (!ReferenceEquals(kv.Value, now))
                        offenders.Add($"{kv.Key} の実体が控え側に入れ替わりました "
                            + "(他所が持っている参照と保存グラフの $ref が外れます)");
                }
            }

            TestSource.AssertScanned(moved, MustBeRestored.Length, "揺らした杭体の項目");

            Assert.AreEqual(0, offenders.Count,
                "キャンセルしても元に戻らない杭体の項目があります。"
                + "支持力と沈下の計算が変わったまま残ります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(25)));
        }

        /// <summary>
        /// キャンセルが杭体入力を戻していること。
        /// (完全性の網だけでは、キャンセルが呼ばなくなったことに気づけない)
        /// </summary>
        [TestMethod]
        public void TheCancel_RestoresThePileBodies()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs");
            string body = TestSource.MethodBody(src, "private void OnCancel()");

            // コメントを落としてから見る。落とさないと、説明文に名前が出ているだけで
            // 通ってしまう (実際にここで空振りした)。
            body = Regex.Replace(body, @"//[^
]*", "");

            StringAssert.Contains(body, "RestoreScalarsFrom(",
                "沈下ウィンドウのキャンセルが杭体入力を戻していません。"
                + "先端径・α・N をいじってキャンセルしても残ります");

            int handWritten = Regex.Matches(body, @"PileBody\w*\.\w+\s*=\s*_prev").Count;
            Assert.AreEqual(0, handWritten,
                $"キャンセルが項目を手で並べています ({handWritten} 件)。"
                + "沈下側の入力が増えたときに取り残されます");
        }

        /// <summary>
        /// 画面が実体へ書く先が、写しの届く範囲 (スカラー) に収まっていること。
        ///
        /// <c>RestoreScalarsFrom</c> は参照型を写さない (写すとコレクションの実体が
        /// 控え側に入れ替わり、他所の参照と保存グラフの $ref が外れる)。
        /// だから画面が参照型を触るようになったら、写しでは戻せない。
        /// </summary>
        [TestMethod]
        public void TheWindow_OnlyWritesScalarsToTheLiveModel()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "SettlementViewModel.cs");
            string xaml = TestSource.Read("Graphics_r1", "Views", "SettlementWindow.xaml");

            var written = new SortedSet<string>(StringComparer.Ordinal);

            // コード側: PileBody.X = / InputModel.PileBodies[...].X =
            foreach (var line in src.Split('\n'))
            {
                string s = line.Trim();
                if (s.StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (Match m in Regex.Matches(s,
                    @"(?:PileBody|InputModel\.PileBodies\[[^\]]*\])\.(\w+)\s*=(?!=)"))
                {
                    written.Add(m.Groups[1].Value);
                }
            }

            // 画面側: 値を書き戻すバインドだけ拾う。
            // ItemsSource (選択肢の一覧) は表示に使うだけで書き込みではないので数えない。
            foreach (Match m in Regex.Matches(xaml,
                @"Text=""\{Binding PileBody\.(\w+)|Path=""PileBody\.(\w+)"""))
            {
                string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                written.Add(name);
            }

            TestSource.AssertScanned(written.Count, MustBeRestored.Length,
                "沈下ウィンドウが杭体入力へ書く項目");

            var outOfReach = new List<string>();
            foreach (string name in written)
            {
                var p = typeof(PileBodyInput).GetProperty(name,
                    BindingFlags.Public | BindingFlags.Instance);
                if (p == null) continue;   // ViewModel 側のプロパティ

                var t = p.PropertyType;
                bool scalar = t.IsValueType || t == typeof(string);

                // 読み取り専用 (選択肢の一覧など) は書き込みではない
                if (!scalar && p.SetMethod?.IsPublic != true) continue;

                if (!scalar)
                    outOfReach.Add($"{name} ({t.Name})");
            }

            Assert.AreEqual(0, outOfReach.Count,
                "沈下ウィンドウが杭体入力の参照型を書き換えています。"
                + "RestoreScalarsFrom はスカラーしか写さないので、キャンセルで戻りません:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", outOfReach));

            // 実体へ書く主要項目が、控えの対象から漏れていないこと
            var missing = MustBeRestored.Where(n => !written.Contains(n)).ToList();
            Assert.AreEqual(0, missing.Count,
                "画面から書かれているはずの項目が見つかりません。"
                + "バインドの書き方か名前が変わった可能性があります (網が空振りします): "
                + string.Join(", ", missing));
        }

        private static void Restore(PileBodyInput target, PileBodyInput saved)
        {
            var m = typeof(PileBodyInput).GetMethod("RestoreScalarsFrom",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(m, "RestoreScalarsFrom が見つかりません (名前が変わった?)");
            m!.Invoke(target, [saved]);
        }

        /// <summary>
        /// 杭体入力<b>自身</b>のスカラーだけを動かす。動かせた個数を返す。
        ///
        /// 子 (セグメント・杭頭) へは潜らない。あちらは沈下ウィンドウの編集対象ではなく、
        /// 写しの対象でもないので、動かすと戻らないのが正しい。
        ///
        /// 文字列は<b>自由記述のものだけ</b>動かす。工法名や断面タイプを壊すと
        /// 別のモデルの話になってしまう。
        /// </summary>
        private static int PerturbOwnScalars(PileBodyInput body)
        {
            var freeText = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(PileBodyInput.PileBodyRef),
                nameof(PileBodyInput.SettleAlphaString),
                nameof(PileBodyInput.SettleNString),
            };

            int seq = 0;
            int moved = 0;

            foreach (var p in typeof(PileBodyInput).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (!p.CanRead || p.SetMethod?.IsPublic != true) continue;

                var t = p.PropertyType;
                object? newValue = null;

                if (t == typeof(double) || t == typeof(double?))
                    newValue = 10.0 + (++seq % 97) + 0.125;
                else if (t == typeof(int) || t == typeof(int?))
                    newValue = 1 + (++seq % 7);
                else if (t == typeof(bool) || t == typeof(bool?))
                {
                    object? cur;
                    try { cur = p.GetValue(body); } catch { continue; }
                    newValue = !(cur is bool b && b);
                }
                else if (t == typeof(string) && freeText.Contains(p.Name))
                    newValue = "揺らした値" + (++seq);

                if (newValue == null) continue;

                object? before, after;
                try { before = p.GetValue(body); } catch { before = null; }
                try { p.SetValue(body, newValue); } catch { continue; }
                try { after = p.GetValue(body); } catch { after = null; }

                if (!Equals(before, after)) moved++;
            }

            return moved;
        }

        /// <summary>戻すべきスカラーの一覧。</summary>
        private static Dictionary<string, object?> Snapshot(PileBodyInput body)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var p in typeof(PileBodyInput).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;

                var t = p.PropertyType;
                if (t != typeof(double) && t != typeof(int) && t != typeof(bool)
                    && t != typeof(string) && !t.IsEnum) continue;

                try { result[p.Name] = p.GetValue(body); } catch { }
            }
            return result;
        }

        /// <summary>写さない約束の参照型メンバー。実体が入れ替わっていないことを見るため。</summary>
        private static Dictionary<string, object?> ReferenceMembers(PileBodyInput body)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var p in typeof(PileBodyInput).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;

                var t = p.PropertyType;
                if (t.IsValueType || t == typeof(string)) continue;

                try
                {
                    var v = p.GetValue(body);
                    if (v != null) result[p.Name] = v;
                }
                catch { }
            }
            return result;
        }
    }
}

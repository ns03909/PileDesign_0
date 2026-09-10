using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// 杭断面ウィンドウの「キャンセル」で、断面が<b>残らず</b>元に戻ること。
    ///
    /// あちらは開いたときに <c>DeepCopy</c> で控えを取り、キャンセル時に書き戻す。
    /// 以前は<b>戻す項目を手で並べて</b>いた (20 項目) ため、PC 鋼材の諸元
    /// (TendonAp・TendonDp・TendonSigmaPy・TendonSigmaPu・Prestress) が抜けており、
    /// <b>既製杭の製品を変えてキャンセルすると PC 鋼材が戻らなかった</b>。
    /// 製品名と杭径だけ戻るので断面が食い違い、N-M 曲線が変わる。
    ///
    /// 一覧を持つ形は諸元が増えるたびに取り残されるので、まるごと写す
    /// <c>PileSection.RestoreFrom</c> にした。その完全性をここで見張る。
    ///
    /// 揺らしてから戻し、<b>全スカラーが元に戻ったか</b>を突き合わせる。
    /// 一覧を持たないので、諸元が増えても自動で対象に入る。
    ///
    /// <para><b>2 本で役割を分けている。</b>
    /// <see cref="Cancelling_RestoresEveryValue"/> は <c>RestoreFrom</c> の完全性を見る
    /// (画面を作らずに済む)。
    /// <see cref="TheCancel_DoesNotEnumeratePropertiesByHand"/> は、キャンセルがそれを
    /// 使っていること (項目を手で並べる形に戻っていないこと) を見る。
    /// 前者だけでは、キャンセルが RestoreFrom を呼ばなくなっても気づけない。</para>
    /// </summary>
    [TestClass]
    public class PileSectionRestoreTests
    {
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]        // 場所打ちRC
        [DataRow("Example3_1", "PileExample3_1")]    // 既製杭 (SC + PRC)。ここで抜けていた
        [DataRow("Example3_5", "PileExample3_5")]    // 鋼管杭
        public void Cancelling_RestoresEveryValue(string groundName, string pileName)
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
            int moved = 0;

            foreach (var section in sections)
            {
                string what = $"{section.PileBodyType}/{section.PileSectionType}";

                // ウィンドウを開いたときの控え
                var saved = section.DeepCopy();
                var before = Snapshot(section);

                // 画面で諸元をいじった状態を作る
                moved += ModelPerturbation.PerturbAll(section);

                // キャンセル
                Restore(section, saved);

                var after = Snapshot(section);
                foreach (var kv in before)
                {
                    if (!after.TryGetValue(kv.Key, out var now)) continue;
                    if (!Equals(kv.Value, now))
                        offenders.Add($"{what}: {kv.Key} 元 {kv.Value} → キャンセル後 {now}");
                }
            }

            TestSource.AssertScanned(moved, 20, $"{groundName} で揺らした諸元");

            Assert.AreEqual(0, offenders.Count,
                "キャンセルしても元に戻らない諸元があります。"
                + "断面が食い違った状態で残り、N-M 曲線が変わります:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders.Take(25)));
        }

        /// <summary>
        /// キャンセルが<b>項目を手で並べていない</b>こと。
        /// 並べる形に戻すと、諸元が増えたときに取り残される。
        /// </summary>
        [TestMethod]
        public void TheCancel_DoesNotEnumeratePropertiesByHand()
        {
            string src = TestSource.Read("Graphics_r1", "ViewModels", "PileSectionViewModel.cs");

            int at = src.IndexOf("private void OnCancel()", StringComparison.Ordinal);
            Assert.IsTrue(at > 0, "OnCancel が見つかりません");

            string body = src[at..Math.Min(src.Length, at + 900)];

            StringAssert.Contains(body, "RestoreFrom",
                "キャンセルがまるごと戻していません");

            int handWritten = System.Text.RegularExpressions.Regex
                .Matches(body, @"PileSection\.\w+\s*=\s*PrevPileSection\.").Count;
            Assert.AreEqual(0, handWritten,
                $"キャンセルが項目を手で並べています ({handWritten} 件)。"
                + "諸元が増えたときに取り残されます");
        }

        private static void Restore(PileSection target, PileSection saved)
        {
            var m = typeof(PileSection).GetMethod("RestoreFrom",
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(m, "RestoreFrom が見つかりません (名前が変わった?)");
            m!.Invoke(target, [saved]);
        }

        /// <summary>書き出される (= 復元すべき) スカラーの一覧。</summary>
        private static Dictionary<string, object?> Snapshot(PileSection section)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var p in typeof(PileSection).GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                if (p.GetMethod == null || !p.GetMethod.IsPublic) continue;
                if (p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;

                var t = p.PropertyType;
                if (t != typeof(double) && t != typeof(int) && t != typeof(bool)
                    && t != typeof(string) && !t.IsEnum) continue;

                try { result[p.Name] = p.GetValue(section); } catch { }
            }
            return result;
        }
    }
}

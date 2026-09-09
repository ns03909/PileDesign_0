using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.Results;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 判定できない値の項目が「OK」と読めないこと。
    ///
    /// 検定の判定はどこも <c>!(応答 &gt; 限界)</c> の形で決めています。
    /// <b>NaN との比較はすべて false なので、この形は応答か限界が NaN のとき
    /// true (= OK) になります。</b>解析が NaN を返した項目や、軸力が耐力曲線の
    /// 範囲外で限界値が引けなかった項目が「合格」と読めてしまいます。
    ///
    /// 算出元は 6 か所あり、そのうち曲げとせん断の 2 か所は手前で
    /// <c>!(allowableM &gt; 0)</c> と弾いていました。残りは弾いていません。
    /// 6 か所すべてで気をつけるより、<c>EvaluationItem.IsOk</c> で一度に塞ぎます。
    ///
    /// 例題では今のところ NaN は届いていません (この変更でゴールデンは動きません)。
    /// これは予防です。
    /// </summary>
    [TestClass]
    public class EvaluationNaNTests
    {
        private static EvaluationItem Item(double response, double limit) => new()
        {
            Kind = EvaluationKind.PileSectionMoment,
            Category = "杭体曲げ",
            Response = response,
            Limit = limit,
            Unit = "kN·m",
            IsOk = !(response > limit),   // 算出元と同じ形
        };

        [TestMethod]
        public void ANaNLimit_IsNotOk()
        {
            var item = Item(response: 100.0, limit: double.NaN);

            Assert.IsFalse(item.IsOk,
                "限界値が引けなかった項目が OK と読めています。"
                + "NaN との比較は false なので !(100 > NaN) が true になります");
            Assert.AreEqual("NG", item.StatusLabel);
        }

        [TestMethod]
        public void ANaNResponse_IsNotOk()
        {
            var item = Item(response: double.NaN, limit: 500.0);

            Assert.IsFalse(item.IsOk,
                "応答値が NaN の項目が OK と読めています");
        }

        [TestMethod]
        public void AnInfiniteValue_IsNotOk()
        {
            Assert.IsFalse(Item(double.PositiveInfinity, 500.0).IsOk);
            Assert.IsFalse(Item(100.0, double.PositiveInfinity).IsOk);
        }

        [TestMethod]
        public void OrdinaryValues_StillJudgeAsBefore()
        {
            Assert.IsTrue(Item(response: 100.0, limit: 500.0).IsOk);
            Assert.IsFalse(Item(response: 600.0, limit: 500.0).IsOk);
            // 境界はちょうど等しいとき OK (「超えたら NG」)
            Assert.IsTrue(Item(response: 500.0, limit: 500.0).IsOk);
        }

        /// <summary>
        /// 判定を組み立てている場所が、想定どおり <c>!(… &gt; …)</c> の形であること。
        /// 別の形 (<c>&lt;=</c> など) に書き換えると NaN の扱いが変わるので、
        /// <c>EvaluationItem.IsOk</c> 側の網が前提を失っていないかを見る。
        /// </summary>
        [TestMethod]
        public void TheProducers_StillDecideTheSameWay()
        {
            var producers = new List<string>();
            int scanned = 0;

            foreach (var dir in new[] { "ViewModels", "Services" })
            {
                var root = Path.Combine(TestSource.Root(), "Graphics_r1", dir);
                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(file), @"IsOk\s*=\s*([^,;\r\n]+)"))
                    {
                        scanned++;
                        producers.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value.Trim()}");
                    }
                }
            }

            TestSource.AssertScanned(scanned, 5, "検定の判定を組み立てている場所");

            // NaN が来たときに true になりうる形かどうかは EvaluationItem 側で塞いでいる。
            // ここは「判定が 1 か所に集約されず散らばっている」ことの記録として数を見る。
            Assert.IsTrue(producers.Count >= 5,
                "判定を組み立てている場所が減っています。集約したなら "
                + "EvaluationItem.IsOk の注記も直してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", producers));
        }
    }
}

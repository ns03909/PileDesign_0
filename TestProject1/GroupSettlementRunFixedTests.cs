using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.ViewModels;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 群杭沈下の結果を解析の実行ごとに固定すること (2026-09-27)。
    ///
    /// 以前は 1 つの結果の実体のケース記録を足し引きし、Clear で空にしていたので、開いている結果の窓や
    /// 作りかけの出力が持っている結果まで途中で変わった。解析・破棄のたびに新しい実体へ差し替える。
    /// 入力を編集したあと沈下だけやり直したときは、最新の沈下を表示し、水平解析と入力の時点が違うことを示す。
    /// </summary>
    [TestClass]
    public class GroupSettlementRunFixedTests
    {
        private static GroupSettlementCaseRecord Rec(string name, bool beamAware = false)
            => new() { LoadCaseName = name, IsBeamAware = beamAware };

        [TestMethod]
        public void WithAndWithoutMakeANewResultAndLeaveTheOldOneAlone()
        {
            var a = Rec("VL");
            var b = Rec("L1", beamAware: true);
            var original = new GroupSettlementResult().With([a, b], 1, "個別矩形（基礎梁考慮）");

            var trimmed = original.Without([a]);
            Assert.AreNotSame(original, trimmed);
            Assert.AreEqual(2, original.CaseRecords.Count, "除いたときに元の結果の中身が変わっています");
            CollectionAssert.AreEqual(new[] { b }, trimmed.CaseRecords.ToList());
            Assert.AreSame(b, trimmed.ActiveRecord, "表示中のケースが残ったのに表示が外れています");

            var noActive = original.Without([b]);
            Assert.AreSame(a, noActive.ActiveRecord, "表示中のケースが消えたら末尾のケースを表示します");
            Assert.AreEqual(-1, original.Without([a, b]).ActiveCaseIndex);

            var replaced = original.With([Rec("new")], 0, "任意矩形");
            Assert.AreEqual(2, original.CaseRecords.Count, "差し替えで元の結果の中身が変わっています");
            Assert.AreEqual("任意矩形", replaced.ActiveLoadingType);
        }

        [TestMethod]
        public void SettingCaseRecordsReplacesTheResultInstance()
        {
            var pgs = new PileGroupSettlement();
            var held = pgs.Result;   // 開いている窓などが持っている結果
            pgs.CaseRecords = [Rec("VL")];

            Assert.AreNotSame(held, pgs.Result, "ケース記録を差し替えても結果の実体が同じです");
            Assert.AreEqual(0, held.CaseRecords.Count, "前の結果を持っている側の中身が変わっています");
            Assert.AreEqual(1, pgs.CaseRecords.Count);
        }

        /// <summary>結果の中身を足し引きする書き方が残っていないこと (読込時の旧形式の移行だけは例外)。</summary>
        [TestMethod]
        public void NobodyMutatesAPublishedResult()
        {
            string dir = TestSource.Dir("Graphics_r1");
            var files = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .ToList();
            TestSource.AssertScanned(files.Count, 300, "本体のソース");

            var offenders = files
                .Where(f => Path.GetFileName(f) is not ("GroupSettlementResult.cs" or "LegacySettlementMigration.cs"))
                .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, i, l)))
                .Where(x => !x.l.TrimStart().StartsWith("//")
                            && Regex.IsMatch(x.l, @"CaseRecords\.(Add|Remove|RemoveAt|Clear|Insert)\(|Result\.Clear\(\)"))
                .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}  {x.l.Trim()}")
                .ToList();
            Assert.AreEqual(0, offenders.Count,
                "群杭沈下の結果の中身をその場で書き換えています (With / Without で新しい結果にして差し替える):\n  "
                + string.Join("\n  ", offenders));
        }

        [TestMethod]
        public void TheStatusSaysWhenSettlementWasSolvedFromAnotherInput()
        {
            string text = MainWindowViewModel.BuildResultSetStatusText("2026-09-27 10:00",
                horizontalStale: true, settlementStale: false, materialOptionsChanged: false, settlementFromOtherInput: true);
            StringAssert.Contains(text, "沈下は水平解析と別の時点の入力で解いた最新の結果を表示しています");

            string plain = MainWindowViewModel.BuildResultSetStatusText("2026-09-27 10:00", false, false, false);
            Assert.IsFalse(plain.Contains("別の時点"));
        }
    }
}

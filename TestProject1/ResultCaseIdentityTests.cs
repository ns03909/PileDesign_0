using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 鉛直に解く計算の結果 (単杭沈下 (基礎梁考慮)・群杭沈下の記録) は、荷重ケースと組合せを別の値 (<see cref="ResultCaseIdentity"/>) で持ち、
    /// 照合はそれで行うこと (2026-11-05)。
    ///
    /// <para>以前は見出しの文字だけを持ち、照合のたびに読み取っていた。ケース名の文字や係数の丸めで読み違え、
    /// 解析のあとに荷重ケースを並べ替えると、番号の同じ別のケースの結果と照合しえた (2026-11-04 外部レビュー)。
    /// これより前に保存した結果は値を持たないので、見出しから読む。</para>
    /// </summary>
    [TestClass]
    public class ResultCaseIdentityTests
    {
        private static InputModel InputWith(params string[] level1Names)
        {
            var cases = new System.Collections.ObjectModel.ObservableCollection<LoadCase>(
                level1Names.Select((name, i) => new LoadCase { Level = 1, No = i + 1, LoadName = name }));
            return new InputModel { LoadCasesInput = new LoadCasesInput { LoadCasesLevel1 = cases, LoadCasesLevel2 = [], LoadCombinations = [] } };
        }

        /// <summary>
        /// 解析のあとに荷重ケースを並べ替えたら、番号の同じ別のケースの結果と照合しない (レベル・番号・名前がすべて一致するものだけ)。
        /// </summary>
        [TestMethod]
        public void AfterReorderingLoadCases_AResultIsNotMatchedToAnotherCaseWithTheSameNumber()
        {
            var u2 = ResultCaseIdentity.Seismic(1, 2, "U2", null);   // 解いたときは U2 が 2 番目
            Assert.IsTrue(SeismicCaseLabels.RefersTo(u2, InputWith("U1", "U2"), "U2"), "(前提) 並べ替える前は一致する");

            var reordered = InputWith("U2", "U1");   // 並べ替えて U2 が 1 番目、U1 が 2 番目
            Assert.IsFalse(SeismicCaseLabels.RefersTo(u2, reordered, "U2"), "番号の変わったケースの結果を、そのケースのものとしています");
            Assert.IsFalse(SeismicCaseLabels.RefersTo(u2, reordered, "U1"), "番号が同じだけの別のケースの結果を、そのケースのものとしています");

            // 見出しだけを持つ以前の結果も同じ (見出しの番号と名前で読む)
            var legacy = SeismicCaseLabels.KeyOf("L1-2: U2", null);
            Assert.IsFalse(SeismicCaseLabels.RefersTo(legacy, reordered, "U1"));
            Assert.IsFalse(SeismicCaseLabels.RefersTo(legacy, reordered, "U2"));
        }

        /// <summary>結果が持つ値を、見出しの文字より優先する (見出しにケース名の文字が紛れても読み違えない)。</summary>
        [TestMethod]
        public void TheStoredKeyIsUsedRatherThanTheHeading()
        {
            var stored = ResultCaseIdentity.Seismic(1, 1, "U1", new LoadCombination(2, 1.0, 1.004, -1.0));
            var key = SeismicCaseLabels.KeyOf("VL", stored);   // 見出しは常時の形でも
            Assert.AreSame(stored, key);
            Assert.IsFalse(key.IsLongTerm);
            Assert.AreEqual(1.004, key.BetaU!.Value, 0.0, "係数が丸まっています");
            Assert.IsTrue(new GroupSettlementCaseRecord { LoadCaseName = "杭軸力 VL", CaseKey = stored }.IsLongTerm == false,
                "記録が持つ値ではなく見出しから常時と読んでいます");
        }

        /// <summary>値を持たない以前の結果は、見出しから読む (読めない見出しはどのケースとも一致しない)。</summary>
        [TestMethod]
        public void ResultsSavedBeforeHaveTheirKeyReadFromTheHeading()
        {
            var seismic = SeismicCaseLabels.KeyOf("L1-1: 地震 VL" + SeismicAxialForces.CombinationSuffix(2, 1.0, -1.0), null);
            Assert.AreEqual((1, 1, "地震 VL", (int?)2, (double?)1.0, (double?)-1.0),
                (seismic.Level, seismic.CaseNo, seismic.CaseName, seismic.CombinationNo, seismic.BetaU, seismic.BetaL));
            foreach (var longTerm in new[] { "VL", "VL (常時+追加)", "杭軸力 VL", "矩形荷重 (VL)" })
                Assert.IsTrue(SeismicCaseLabels.KeyOf(longTerm, null).IsLongTerm, longTerm);

            var unknown = SeismicCaseLabels.KeyOf("L1-1", null);   // 前置きの形でない以前の見出し
            Assert.AreEqual(-1, unknown.Level);
            Assert.IsFalse(SeismicCaseLabels.RefersTo(unknown, null, "VL"));
            Assert.IsFalse(SeismicCaseLabels.RefersTo(unknown, null, "L1-1"));
        }

        /// <summary>保存して開き直しても、記録の荷重ケース・組合せ (丸めない係数を含む) が残ること。</summary>
        [TestMethod]
        public void TheKeySurvivesSavingAndReopening()
        {
            var (live, _) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (live == null) { Assert.Inconclusive("例題ファイルなし"); return; }
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };

            live.PileGroupSettlement ??= new PileGroupSettlement();
            live.PileGroupSettlement.CaseRecords.Add(new GroupSettlementCaseRecord
            {
                LoadCaseName = "L1-2: U2 / 組合せNo.2 (βU=1.004, βL=-1)",
                CaseKey = ResultCaseIdentity.Seismic(1, 2, "U2", new LoadCombination(2, 1.0, 1.004, -1.0)),
                LoadingType = "個別矩形（基礎梁考慮）",
                IsBeamAware = true,
                IsConverged = true,
                PileSettlements_mm = new Dictionary<int, double> { [live.PileLayoutItems[0].PileNo] = 5.0 },
            });

            string path = Path.Combine(Path.GetTempPath(), $"pd_casekey_{Guid.NewGuid():N}.pdj");
            try
            {
                new FileOperationService(options).SaveProjectData(path, live, anaModel: null);
                var loaded = JsonSerializer.Deserialize<ProjectData>(FileOperationService.ReadSaveFileText(path), options)!;
                var key = loaded.GroupSettlementResult!.CaseRecords.Single(r => r.IsBeamAware).CaseKey;
                Assert.IsNotNull(key, "記録の荷重ケース・組合せが保存されていません");
                Assert.AreEqual((1, 2, "U2", (int?)2, (double?)1.004, (double?)-1.0),
                    (key!.Level, key.CaseNo, key.CaseName, key.CombinationNo, key.BetaU, key.BetaL));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>結果・記録を作る所はすべて荷重ケース・組合せ (CaseKey) を持たせること (持たせ忘れると見出しの読み取りに戻る)。</summary>
        [TestMethod]
        public void EveryPlaceThatCreatesAResultSetsTheKey()
        {
            var creation = new Regex(@"new\s+(FEM\.)?(GroupSettlementCaseRecord|VerticalBeamCaseResult|GroupSettlementWithBeamCaseResult)\b");
            var missing = new List<string>();
            int scanned = 0;
            foreach (var file in Directory.GetFiles(TestSource.Dir("Graphics_r1"), "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!creation.IsMatch(lines[i].Split("//")[0])) continue;
                    scanned++;
                    // 初期化子の中 (同じ行から 4 行先まで) に CaseKey があること
                    bool sets = lines.Skip(i).Take(5).Any(l => l.Contains("CaseKey", StringComparison.Ordinal));
                    if (!sets) missing.Add($"{Path.GetFileName(file)}:{i + 1}");
                }
            }
            TestSource.AssertScanned(scanned, 7, "結果・記録を作る所");
            Assert.AreEqual(0, missing.Count, "荷重ケース・組合せを持たせていません: " + string.Join(", ", missing));
        }
    }
}

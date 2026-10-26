using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Output;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// <b>耐震性能グレード × 荷重レベル → 限界状態</b>の規則 (<see cref="LimitStateRule"/>) と、
    /// 図・計算書に描く曲線の軸力の規則 (<see cref="EvaluationService.ResolveCheckAxialForce"/>) が 1 か所にあること。
    ///
    /// どちらも以前は検定・画面のグラフ・計算書に 7〜8 か所ずつ書き写されていて、写しごとに
    /// <list type="bullet">
    /// <item>グレードの既定 (画面のグラフは「A でなければ S」、検定と計算書は「S でなければ A」)</item>
    /// <item>損傷限界の曲線のレベル (グレードS の図はレベル1 の点にもレベル2 の曲線だけ)</item>
    /// <item>地震時軸力が未入力のときの軸力 (検定は常時軸力、図は解析中に書き換わる杭配置の AxialForce =
    ///   直前に解いたケースの値)</item>
    /// </list>
    /// が違っていた。
    /// </summary>
    [TestClass]
    public class LimitStateRuleTests
    {
        private const SectionLimitState Svc = SectionLimitState.Service;
        private const SectionLimitState Dmg = SectionLimitState.Damage;
        private const SectionLimitState Ult = SectionLimitState.Ultimate;

        // ── 規則の表 ─────────────────────────────

        /// <summary>
        /// グレード (A / S / 未設定・想定外) × レベル (長期 / 1 / 2) × 照査の種類 の表。
        /// 未設定・想定外のグレードは A (検定がそうしていた。画面のグラフだけ S 扱いだった)。
        /// </summary>
        [TestMethod]
        public void Table_GradeByLevelByKind()
        {
            // (グレード, レベル, 限界状態, 損傷限界の曲線のレベル)。杭体の曲げ・せん断・変形角・支持力・基礎梁の傾斜角
            var gradeDependent = new (string? Grade, int Level, SectionLimitState Limit, int DamageLevel)[]
            {
                ("A", 0, Svc, 2), ("A", 1, Dmg, 1), ("A", 2, Ult, 2),
                ("S", 0, Svc, 2), ("S", 1, Dmg, 1), ("S", 2, Dmg, 2),
                (null, 0, Svc, 2), (null, 1, Dmg, 1), (null, 2, Ult, 2),
                ("", 0, Svc, 2), ("", 1, Dmg, 1), ("", 2, Ult, 2),
                ("B", 0, Svc, 2), ("B", 1, Dmg, 1), ("B", 2, Ult, 2),
                ("s", 0, Svc, 2), ("s", 1, Dmg, 1), ("s", 2, Ult, 2),   // 検定は "S" と完全一致で見ていた
            };
            var gradeDependentKinds = new[]
            {
                LimitCheckKind.SectionForce, LimitCheckKind.DeformationAngle,
                LimitCheckKind.Bearing, LimitCheckKind.FoundationBeamInclination,
            };
            foreach (var kind in gradeDependentKinds)
                foreach (var row in gradeDependent)
                    Assert.AreEqual(new LimitStateAssignment(row.Limit, row.DamageLevel), LimitStateRule.For(row.Grade, row.Level, kind),
                        $"グレード {row.Grade ?? "(なし)"} / レベル{row.Level} / {kind}");

            // 杭頭回転角は工法の規定。グレードに依らずレベル2 は安全限界と名乗る (従来どおり)
            foreach (var grade in new[] { "A", "S", null })
            {
                Assert.AreEqual(new LimitStateAssignment(Dmg, 1), LimitStateRule.For(grade, 1, LimitCheckKind.PileHeadRotation));
                Assert.AreEqual(new LimitStateAssignment(Ult, 2), LimitStateRule.For(grade, 2, LimitCheckKind.PileHeadRotation));
            }

            // 種類の数が増えたら、上の表にも載せること
            Assert.AreEqual(gradeDependentKinds.Length + 1, Enum.GetValues<LimitCheckKind>().Length,
                "照査の種類が増えています。グレードに従うかどうかを決めてこの表に載せてください");

            // 基本設定から引く形も同じ
            Assert.AreEqual(LimitStateRule.For("S", 2), LimitStateRule.For(new FundamentalInput { SeismicGrade = "S" }, 2));
            Assert.AreEqual(LimitStateRule.For("A", 2), LimitStateRule.For((FundamentalInput?)null, 2));
            Assert.AreEqual("A", LimitStateRule.GradeOf(null));
            Assert.AreEqual("A", LimitStateRule.GradeOf(new FundamentalInput { SeismicGrade = "B" }));
            Assert.AreEqual("S", LimitStateRule.GradeOf(new FundamentalInput { SeismicGrade = "S" }));
        }

        /// <summary>全レベルを 1 枚に描く図の損傷限界の曲線のレベル (照査するレベルをすべて描く)。</summary>
        [TestMethod]
        public void FigureDamageLevels_CoverEveryLevelCheckedAtDamage()
        {
            foreach (var grade in new[] { "A", "S", null, "B" })
            {
                var checkedAtDamage = new[] { 1, 2 }.Where(l => LimitStateRule.For(grade, l).Limit == Dmg).ToList();
                CollectionAssert.AreEquivalent(checkedAtDamage, LimitStateRule.DamageCheckedLevels(grade).ToList(), $"グレード {grade}");
                CollectionAssert.Contains(checkedAtDamage, LimitStateRule.PrimaryDamageLevel(grade));
                foreach (int lv in checkedAtDamage)
                    Assert.AreEqual(LimitStateRule.For(grade, lv).DamageLevel, lv, "図の曲線のレベルが検定の曲線のレベルと違います");
            }
            Assert.AreEqual(1, LimitStateRule.DamageCurveLevel(0));
            Assert.AreEqual(1, LimitStateRule.DamageCurveLevel(1));
            Assert.AreEqual(2, LimitStateRule.DamageCurveLevel(2));
        }

        /// <summary>画面の「限界状態」の選択肢はすべて読み替えられる (「なし」以外)。選択肢はソースから拾う。</summary>
        [TestMethod]
        public void EveryLimitStateOption_IsParsed()
        {
            string options = TestSource.Region(
                TestSource.Read("Graphics_r1", "ViewModels", "GraphViewModel.Options.cs"),
                "public string[] LimitStateOptions", 600);
            var labels = Regex.Matches(options, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            TestSource.AssertScanned(labels.Count, 7, "限界状態の選択肢");
            var seen = new HashSet<(bool, SectionLimitState)>();
            foreach (var label in labels)
            {
                bool ok = LimitStateRule.TryParseCurveOption(label, out bool factored, out var limit);
                if (label == "なし") { Assert.IsFalse(ok); continue; }
                Assert.IsTrue(ok, $"選択肢「{label}」を限界状態に読み替えられません");
                Assert.AreEqual(label.StartsWith("低減後", StringComparison.Ordinal), factored, label);
                StringAssert.Contains(label, LimitStateRule.Name(limit), "読み替えた限界状態が選択肢の名前と違います");
                Assert.IsTrue(seen.Add((factored, limit)), $"同じ曲線に 2 つの選択肢: {label}");
            }
            Assert.AreEqual(6, seen.Count);
        }

        // ── ソースの走査 (写しを作らせない) ─────────────────────────────

        /// <summary>
        /// グレード × レベル → 限界状態・損傷限界のレベルを決める書き方は <see cref="LimitStateRule"/> にだけある。
        /// 写しは直すと取り残される (実際に 8 か所あり、既定と損傷限界のレベルが写しごとに違った)。
        /// </summary>
        [TestMethod]
        public void NoOtherFileDecidesLimitStateFromGradeOrLevel()
        {
            var banned = new (string What, Regex Pattern)[]
            {
                ("耐震グレードの文字列比較", new Regex(@"[Gg]rade\w*\s*\)?\s*[!=]=\s*""[AS]""")),
                ("耐震グレードの文字列比較", new Regex(@"""[AS]""\s*[!=]=\s*[\w.?]*[Gg]rade")),
                ("レベルから損傷限界のレベルを決める式 (== 1 ? 1 : 2)", new Regex(@"==\s*1\s*\?\s*1\s*:\s*2\b")),
                ("レベルから損傷限界のレベルを決める式 (Math.Max(level, 1))", new Regex(@"Math\.Max\(\s*\w*[Ll]evel\w*\s*,\s*1\s*\)")),
                ("レベルから限界状態の名前を決める式", new Regex(@"[Ll]evel\w*\s*==\s*[012]\s*\?\s*""(使用|損傷|安全|終局)限界")),
                ("限界状態の選択肢の文字列で曲線を選ぶ switch", new Regex(@"""低減[前後](使用|損傷|安全)限界状態""\s*=>")),
            };

            var violations = new List<string>();
            int scanned = 0;
            foreach (var (rel, code) in AppSources())
            {
                scanned++;
                if (rel == "Services/LimitStateRule.cs") continue;
                foreach (var (what, pattern) in banned)
                    foreach (Match m in pattern.Matches(code))
                        violations.Add($"{rel}: {what}: {m.Value}");
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");
            Assert.AreEqual(0, violations.Count,
                "限界状態の規則を LimitStateRule 以外で決めています (LimitStateRule.For / DamageCurveLevel / TryParseCurveOption を使ってください):\n"
                + string.Join("\n", violations));

            // 規則の側には実際にその判定がある (ガードが空振りしていないこと)
            string rule = StripComments(TestSource.Read("Graphics_r1", "Services", "LimitStateRule.cs"));
            Assert.IsTrue(banned[0].Pattern.IsMatch("grade == \"S\"") && rule.Contains("grade == \"S\"", StringComparison.Ordinal),
                "(前提) LimitStateRule にグレードの判定が見つかりません");
            Assert.IsTrue(banned[5].Pattern.IsMatch(@"""低減後損傷限界状態"" =>"), "(前提) 選択肢の switch を捕まえる式が働いていません");
        }

        /// <summary>
        /// 図・計算書・表の軸力は 1 つの規則 (PileLayoutDataItem.ResolveDesignAxialForce と、それを包む
        /// EvaluationService.ResolveCheckAxialForce) を通す。
        ///
        /// 地震時軸力を直に引く (<c>GetSeismicAxialForce</c>) のはモデル・解析・その規則だけ。
        /// 杭配置の <c>AxialForce</c> は解析中に書き換わる値 (直前に解いたケースの値が残る) なので、
        /// モデル・解析の外で読まない (以前は図 6 か所と結果の表 1 か所がこれに落ちていた)。
        /// </summary>
        [TestMethod]
        public void AxialForceForPlots_GoesThroughOneResolver()
        {
            static bool IsModelOrSolver(string rel) =>
                rel == "Models/InputData/PileLayoutDataItem.cs"
                || rel == "Models/InputData/SeismicAxialForces.cs"   // 地震時軸力の窓口 (分離入力の組合せごとの軸力もここ)
                || rel.StartsWith("FEM/", StringComparison.Ordinal)
                || Regex.IsMatch(rel, @"^ViewModels/HorizontalCalculationViewModel(\.\w+)?\.cs$");

            // 解析中に書き換わる値ではない、別の型の AxialForce (検定項目・Chang の入力・複製の初期化)
            var otherTypes = new HashSet<(string File, string Receiver)>
            {
                ("Models/Results/EvaluationTextFormatter.cs", "item"),
                ("Output/WordDocument.SummaryTables.cs", "item"),
                ("ViewModels/ChangViewModel.cs", "newChang"),
                ("Services/PileLayoutService.cs", "copy"),
            };

            var seismic = new Regex(@"\bGetSeismicAxialForce\s*\(");
            var mutable = new Regex(@"\b(\w+)\s*\??\.AxialForce(?!\w)");
            var violations = new List<string>();
            int scanned = 0, allowedSeismic = 0;
            foreach (var (rel, code) in AppSources())
            {
                scanned++;
                bool modelOrSolver = IsModelOrSolver(rel);
                foreach (Match m in seismic.Matches(code))
                {
                    if (modelOrSolver || rel == "ViewModels/EvaluationService.cs") { allowedSeismic++; continue; }
                    violations.Add($"{rel}: 地震時軸力を直に引いています: {Line(code, m.Index)}");
                }
                if (modelOrSolver) continue;
                foreach (Match m in mutable.Matches(code))
                {
                    if (otherTypes.Contains((rel, m.Groups[1].Value))) continue;
                    violations.Add($"{rel}: 解析中に書き換わる AxialForce を読んでいます: {Line(code, m.Index)}");
                }
            }
            TestSource.AssertScanned(scanned, 300, "本体のソース");
            TestSource.AssertScanned(allowedSeismic, 3, "地震時軸力を引く正規の箇所 (モデル・解析・検定の規則)");
            Assert.AreEqual(0, violations.Count,
                "図・計算書・表の軸力は EvaluationService.ResolveCheckAxialForce (検定と同じ) か "
                + "SeismicAxialForces.Design を通してください:\n" + string.Join("\n", violations));
        }

        // ── 振る舞い: 画面・計算書が検定と同じ曲線を選ぶ ─────────────────────────────

        private static MainWindowViewModel? _vm;
        private static bool _loaded;

        /// <summary>場所打ち RC 杭 18 本の例題 (計算例9) を解析したもの (EvaluationBasisTests と同じ条件)。</summary>
        private static MainWindowViewModel Analyzed()
        {
            if (!_loaded)
            {
                _loaded = true;
                try
                {
                    _vm = HeadlessHorizontalRunner.RunExampleForViewModel("Example9", "PileExample9", new HeadlessHorizontalRunner.RunOptions
                    {
                        Level1Steps = 4,
                        Level2Steps = 8,
                        LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.Yes,
                        UseLineSearch = true,
                        Parallelism = 1,
                    });
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗")) { _vm = null; }
            }
            if (_vm == null) Assert.Inconclusive("例題ファイルがありません");
            _vm!.ApplyConcreteModelOptions();
            return _vm;
        }

        /// <summary>
        /// グレード A・S のどちらでも、検定の各行の限界状態は規則どおりで、
        /// <list type="bullet">
        /// <item>計算書の杭の応力図に重ねる限界線 (BuildPileLimitStateStepLines) が検定の限界値と同じ</item>
        /// <item>画面の深さ分布図で検定と同じ限界状態を選んだときの曲線 (GetLimitStateNMCurve / NQCurve) が検定の限界値を再現する</item>
        /// <item>N-M・Q-N 図 (画面・計算書) に、検定が使う損傷限界のレベルの曲線がすべて描かれる</item>
        /// </list>
        /// </summary>
        [TestMethod]
        public void GraphAndReport_PickTheCurveTheCheckUses()
        {
            var vm = Analyzed();
            var input = vm.ResultInputModel!;
            string original = input.FundamentalInput!.SeismicGrade;
            try
            {
                foreach (var grade in new[] { "A", "S" })
                {
                    input.FundamentalInput.SeismicGrade = grade;
                    var items = EvaluationService.BuildEvaluationResult(vm, factored: true).Items
                        .Where(i => i.IsJudged && i.Kind is EvaluationKind.PileSectionMoment or EvaluationKind.PileSectionShear
                                    && i.Level is 1 or 2 && double.IsFinite(i.Limit) && i.Limit > 0)
                        .ToList();
                    TestSource.AssertScanned(items.Count, 100, $"グレード{grade} の杭体の曲げ・せん断の検定");

                    // 検定の限界状態は規則どおり
                    foreach (var item in items)
                        Assert.AreEqual(LimitStateRule.Name(LimitStateRule.For(grade, item.Level).Limit), item.LimitName,
                            $"グレード{grade} レベル{item.Level}: 検定の限界状態が規則と違います");
                    if (grade == "S")
                        Assert.IsTrue(items.Any(i => i.Level == 2 && i.LimitName == "損傷限界"), "(前提) グレードS のレベル2 に損傷限界の行がありません");

                    // N-M・Q-N 図に描く損傷限界のレベルは、検定が損傷限界で照査するレベルをすべて含む
                    var damageLevels = items.Where(i => i.LimitName == "損傷限界").Select(i => i.Level).Distinct().ToList();
                    foreach (int lv in damageLevels)
                        CollectionAssert.Contains(LimitStateRule.DamageCheckedLevels(grade).ToList(), lv,
                            $"グレード{grade}: レベル{lv} の損傷限界の曲線が N-M・Q-N 図に描かれません");

                    CompareReportAndGraphWithCheck(vm, grade, items);
                }
            }
            finally
            {
                input.FundamentalInput.SeismicGrade = original;
            }
        }

        private static void CompareReportAndGraphWithCheck(MainWindowViewModel vm, string grade, List<EvaluationItem> items)
        {
            var input = vm.ResultInputModel!;
            var model = vm.CurrentModel!;
            var doc = new WordDocument(input, model, vm);
            var cases = input.LoadCasesInput.AllSeismicLoadCases;
            var combs = input.LoadCasesInput.LoadCombinations;
            int compared = 0;

            // 断面の計算をやり直すので、(杭, ケース, 組合せ, 液状化) の組を絞る
            foreach (var g in items.GroupBy(i => (i.PileNo, i.Level, i.LoadCaseNo, i.LoadCombinationNo, i.IsLiquefaction)).Take(12))
            {
                var pile = input.PileLayoutItems.First(p => p.PileNo == g.Key.PileNo);
                var lc = cases.First(c => c.Level == g.Key.Level && c.No == g.Key.LoadCaseNo);
                var comb = combs.First(c => c.No == g.Key.LoadCombinationNo);
                var (shearXs, shearZs, momentXs, momentZs, _) = doc.BuildPileLimitStateStepLines(pile, lc, comb, g.Key.IsLiquefaction!.Value);

                // 要素の i 端の Z → 計算書の限界線の値 (各要素で 2 点: i 端・j 端の順)
                static Dictionary<double, double> ByStartZ(List<double> xs, List<double> zs)
                {
                    var d = new Dictionary<double, double>();
                    for (int k = 0; k + 1 < xs.Count; k += 2) d.TryAdd(zs[k], xs[k]);
                    return d;
                }
                var momentByZ = ByStartZ(momentXs, momentZs);
                var shearByZ = ByStartZ(shearXs, shearZs);

                foreach (var item in g.Where(i => i.EndLabel == "i端").Take(6))
                {
                    // 要素の名前は杭の中で重なる ("beam") ので、区間の番号で引く
                    var beam = pile.Beams.First(b => b.SegmentIndex == item.SegmentIndex);
                    var section = pile.SoilPile!.PileBodySegments[beam.SegmentIndex!.Value].PileSection!;
                    double z = beam.NodeI.Coord.Z;
                    var rule = LimitStateRule.For(grade, item.Level);
                    string option = $"低減後{LimitStateRule.Name(rule.Limit)}状態";
                    double n = item.AxialForce!.Value;

                    if (item.Kind == EvaluationKind.PileSectionMoment)
                    {
                        Assert.IsTrue(momentByZ.TryGetValue(z, out double report), $"計算書の曲げの限界線に {item.TargetName} がありません");
                        Assert.AreEqual(item.Limit, report, 1e-6 * item.Limit,
                            $"グレード{grade} {item.Category} {item.TargetName} {item.LoadCaseName}: 計算書の限界線が検定の限界値と違います");

                        var (gn, gm) = GraphViewModel.GetLimitStateNMCurve(section, option, item.Level);
                        Assert.AreEqual(item.Limit, PileSection.LocateLimitAtAxialForce(gn, gm, n).Value, 1e-6 * item.Limit,
                            $"グレード{grade} {item.TargetName}: 画面の「{option}」の曲線が検定の曲線と違います");
                    }
                    else
                    {
                        double magnification = rule.Limit == Dmg ? section.ShearDesignMagnification : 1.0;
                        Assert.IsTrue(shearByZ.TryGetValue(z, out double report), $"計算書のせん断の限界線に {item.TargetName} がありません");
                        Assert.AreEqual(item.Limit / magnification, report, 1e-6 * item.Limit,
                            $"グレード{grade} {item.Category} {item.TargetName} {item.LoadCaseName}: 計算書の限界線が検定の限界値と違います");

                        var (gn, gq) = GraphViewModel.GetLimitStateNQCurve(section, option, item.Level, Math.Round(item.MonQd ?? PileSection.DefaultMonQd, 3));
                        Assert.AreEqual(item.Limit, PileSection.LocateLimitAtAxialForce(gn, gq, n).Value, 1e-6 * item.Limit,
                            $"グレード{grade} {item.TargetName}: 画面の「{option}」の曲線が検定の曲線と違います");
                    }
                    compared++;
                }
            }
            TestSource.AssertScanned(compared, 10, $"グレード{grade} で照合した行");
        }

        /// <summary>
        /// 画面の N-M・Q-N 図の「性能グレード」の既定は、検定と同じく基本設定のグレード (未設定・想定外なら A)。
        /// 以前は入力に依らず A で、グレードS のモデルでも安全限界の曲線が既定で描かれていた。
        /// </summary>
        [TestMethod]
        public void GraphGradeDefault_FollowsTheInputGrade()
        {
            var vm = Analyzed();
            var input = vm.ResultInputModel!;
            string original = input.FundamentalInput!.SeismicGrade;
            try
            {
                foreach (var (grade, expectS) in new[] { ("S", true), ("A", false), ("B", false) })
                {
                    input.FundamentalInput.SeismicGrade = grade;
                    var graph = new GraphViewModel(vm);
                    Assert.AreEqual(expectS, graph.IsSeismicGradeS, $"基本設定のグレード {grade}: 図の既定のグレードが検定と違います");
                    Assert.AreEqual(!expectS, graph.IsSeismicGradeA, $"基本設定のグレード {grade}");
                    Assert.AreEqual(LimitStateRule.For(grade, 2), LimitStateRule.For(graph.SelectedSeismicGrade, 2),
                        $"基本設定のグレード {grade}: 図のレベル2 の限界状態が検定と違います");
                }
            }
            finally
            {
                input.FundamentalInput.SeismicGrade = original;
            }
        }

        /// <summary>
        /// 結果の表 (M-φ 曲線) の「N入力」は、その表の荷重ケースの設計軸力。
        /// 以前は杭配置の AxialForce (解析中に書き換わり、最後に解いたケースの値が残る) を載せていて、
        /// どの荷重ケースの表にも同じ値が出ていた。
        /// </summary>
        [TestMethod]
        public void MPhiTable_InputAxialForce_IsThatCasesDesignAxialForce()
        {
            var vm = Analyzed();
            var input = vm.ResultInputModel!;
            var model = vm.CurrentModel!;
            var service = new AnalysisResultTableService();

            // この例題は地震時軸力が全ケースで同じなので、ケースごとに違う値を入れる
            // (同じだと、最後に解いたケースの値でも合格してしまう)。表は入力を読むだけなので解き直さない
            var saved = input.PileLayoutItems.ToDictionary(p => p, p => (L1: p.AxialForceLevel1s.ToList(), L2: p.AxialForceLevel2s.ToList()));
            try
            {
                foreach (var p in input.PileLayoutItems)
                {
                    for (int k = 0; k < p.AxialForceLevel1s.Count; k++) p.AxialForceLevel1s[k] = 1000.0 + 37.0 * (k + 1) + p.No;
                    for (int k = 0; k < p.AxialForceLevel2s.Count; k++) p.AxialForceLevel2s[k] = 2000.0 + 53.0 * (k + 1) + p.No;
                }

                var steps = model.AnalysisStepResults
                    .GroupBy(r => AnaModel.CaseConvergenceKey(r.LoadCase, r.LoadCombination, r.IsLiquefaction))
                    .Select(g => g.OrderByDescending(r => r.Step).First())
                    .Where(r => r.LoadCase?.Level is 1 or 2)
                    .ToList();
                var distinctDesign = new HashSet<string>();
                int compared = 0;
                foreach (var s in steps)
                {
                    var table = service.BuildTables(model, s.LoadCase, s.LoadCombination, s.IsLiquefaction, s.Step, input)
                        .FirstOrDefault(t => t.Category == "MPhiCurve");
                    if (table == null) continue;
                    foreach (var row in table.Rows.OfType<MPhiCurveRow>().Where(r => r.PointIndex == 1))
                    {
                        var pile = input.PileLayoutItems.First(p => p.No == row.PileNo);
                        double expected = pile.ResolveDesignAxialForce(s.LoadCase!.No, s.LoadCase.Level).N;
                        Assert.AreEqual(expected, row.InputAxialForce, 1e-9,
                            $"杭{row.PileNo} {s.LoadCase.LoadName}: 表の「N入力」がこのケースの設計軸力ではありません");
                        distinctDesign.Add($"{row.PileNo}:{expected:R}");
                        compared++;
                    }
                }
                TestSource.AssertScanned(compared, 20, "M-φ の表の行");
                int piles = distinctDesign.Select(k => k.Split(':')[0]).Distinct().Count();
                Assert.IsTrue(distinctDesign.Count > piles, "(前提) ケースで設計軸力が違う杭がありません");
            }
            finally
            {
                foreach (var (p, v) in saved)
                {
                    for (int k = 0; k < v.L1.Count; k++) p.AxialForceLevel1s[k] = v.L1[k];
                    for (int k = 0; k < v.L2.Count; k++) p.AxialForceLevel2s[k] = v.L2[k];
                }
            }
        }

        // ── 道具 ─────────────────────────────

        /// <summary>本体のソース (コメントを除く)。パスは Graphics_r1 からの相対で区切りは '/'。</summary>
        private static IEnumerable<(string Rel, string Code)> AppSources()
        {
            string root = TestSource.Dir("Graphics_r1");
            foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (rel.StartsWith("obj/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal)) continue;
                yield return (rel, StripComments(File.ReadAllText(path)));
            }
        }

        /// <summary>コメント (// と /* */) を取り除く。コメントアウトした古いコードを違反と数えないため。</summary>
        private static string StripComments(string code)
        {
            code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return Regex.Replace(code, @"//[^\r\n]*", "");
        }

        private static string Line(string code, int index)
        {
            int s = code.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int e = code.IndexOf('\n', index);
            return code[s..(e < 0 ? code.Length : e)].Trim();
        }
    }
}

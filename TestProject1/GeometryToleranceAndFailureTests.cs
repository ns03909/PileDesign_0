using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1;

/// <summary>
/// 長さ 0 の判定基準の共有 (<see cref="GeometryTolerance"/>) と、解析が止まったときの知らせ (<see cref="AnalysisFailure"/>)。
/// </summary>
[TestClass]
[DoNotParallelize]
public class GeometryToleranceAndFailureTests
{
    private bool _unattended;
    [TestInitialize] public void Init() { _unattended = MessageService.IsUnattended; MessageService.IsUnattended = true; }
    [TestCleanup] public void Cleanup() => MessageService.IsUnattended = _unattended;

    [TestMethod]
    public void ZeroLength_IsJudgedByOneThreshold()
    {
        Assert.IsTrue(GeometryTolerance.IsZeroLength(0));
        Assert.IsTrue(GeometryTolerance.IsZeroLength(5e-7));
        Assert.IsFalse(GeometryTolerance.IsZeroLength(GeometryTolerance.MinMemberLength));
        Assert.IsFalse(GeometryTolerance.IsZeroLength(0.001));
    }

    /// <summary>
    /// 入力の検査・基礎梁の自動生成・杭要素の生成・検定・杭頭変形角・梁の分割が、同じ基準を使うこと。
    /// 以前は 1e-6・1e-9・1e-10 と別々で、自動生成した梁 (1e-9〜1e-6 m) を検査が止める食い違いがあり得た。
    /// </summary>
    [TestMethod]
    public void EveryLengthJudgement_SharesTheThreshold()
    {
        var uses = new[]
        {
            ("Services", "ModelConnectivityCheck.cs"),
            ("FEM", "AnalysisModelling.cs"),
            ("ViewModels", "MainWindowViewModel.ModelEditing.cs"),
            ("ViewModels", "EvaluationService.cs"),
            ("Services", "PileHeadDeformationAngle.cs"),
            ("ViewModels", "MainWindowViewModel.IntersectionSearch.cs"),
        };
        var missing = uses.Where(u => !TestSource.Read("Graphics_r1", u.Item1, u.Item2).Contains("GeometryTolerance."))
                          .Select(u => u.Item2).ToList();
        Assert.AreEqual(0, missing.Count, "長さ 0 の判定に共通の基準を使っていません: " + string.Join(", ", missing));
        Assert.AreEqual(GeometryTolerance.MinMemberLength, MainWindowViewModel.SplitPointDistanceTolerance);
    }

    // ── 解析が止まったときの知らせ ──

    /// <summary>杭の番号は、例外が持つ問題 (<see cref="DiagnosticException"/>) から取る。包まれていても辿る。</summary>
    [TestMethod]
    public void PileNumbers_AreTakenFromTheDiagnostics()
    {
        var ex = new InvalidOperationException("モデル作成に失敗しました",
            new DiagnosticException("杭要素作成エラー", [
                Diagnostic.AnalysisAt(DiagnosticTarget.Pile(12), "a"),
                Diagnostic.AnalysisAt(DiagnosticTarget.Pile(7), "b")]));
        CollectionAssert.AreEqual(new[] { 12, 7 }, AnalysisFailure.PileNosIn(ex).ToArray());
        Assert.AreEqual(0, AnalysisFailure.PileNosIn(new Exception("行列が特異です")).Count);
    }

    /// <summary>
    /// <b>本題。</b> 止まったケース・段階・理由・関係する杭を知らせること。
    /// ケースは並列に解くので AggregateException に包まれていても辿る。
    /// </summary>
    [TestMethod]
    public void Describe_ShowsCaseStageReasonAndPiles()
    {
        var inner = new DiagnosticException("剛性行列が特異です (杭節点-3-10)\n詳細",
            [Diagnostic.AnalysisAt(DiagnosticTarget.Pile(3), "剛性行列が特異です")]);
        var failed = new AnalysisCaseFailedException("L2 X+ 液状化無", "荷重ステップ 4/12 の反復", inner);
        string text = AnalysisFailure.Describe(new AggregateException(failed));

        StringAssert.Contains(text, "荷重ケース: L2 X+ 液状化無");
        StringAssert.Contains(text, "段階: 荷重ステップ 4/12 の反復");
        StringAssert.Contains(text, "理由: 剛性行列が特異です (杭節点-3-10)");
        StringAssert.Contains(text, "関係する場所: 杭 No.3");
        Assert.IsFalse(text.Contains("詳細\n"), "例外の 2 行目以降まで出している");
    }

    /// <summary>ケースの外 (モデル作成など) で止まったときも、理由は出す (ケースと段階は無い)。場所が無ければログへ案内する。</summary>
    [TestMethod]
    public void Describe_WithoutACase_StillShowsTheReason()
    {
        string text = AnalysisFailure.Describe(new InvalidOperationException("杭No.5 の地盤が見つかりません"));
        StringAssert.Contains(text, "理由: 杭No.5 の地盤が見つかりません");
        Assert.IsFalse(text.Contains("荷重ケース:"));
        StringAssert.Contains(text, "特定できませんでした");
    }

    /// <summary>関係する杭をメイン画面で選ぶ (直す場所へ案内する)。ほかの選択は外す。</summary>
    [TestMethod]
    public void SelectPilesForReview_SelectsOnlyThosePiles()
    {
        var input = new InputModel();
        var vm = new MainWindowViewModel { CurrentInputModel = input };
        input.PileLayoutItems ??= [];
        for (int i = 1; i <= 4; i++) input.PileLayoutItems.Add(new PileLayoutDataItem { No = i, PileNo = i, X = i });
        input.PileLayoutItems[0].IsSelected = true;

        Assert.AreEqual(2, vm.SelectPilesForReview([2, 4]));
        CollectionAssert.AreEqual(new[] { false, true, false, true },
            input.PileLayoutItems.Select(p => p.IsSelected).ToArray());
        Assert.AreEqual(0, vm.SelectPilesForReview([]), "杭の番号が無いのに選択を変えている");
    }
}

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
}

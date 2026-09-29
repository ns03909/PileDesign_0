using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.Services;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1;

/// <summary>
/// ベクトルの大きさ √(x²+y²(+z²)) を、アプリ全体で同じ安定した計算 (<see cref="StableNumerics.Norm(double, double)"/>) にそろえること。
///
/// 二乗してから足すと、大きな有限の値 (1e155 程度より大きい) で途中が無限大になり、小さな値では 0 に潰れる。
/// 結果の表・画面の描画・ツールチップ・計算書・モデルの検査で、同じ形の計算が 100 か所ほど散らばっていた。
/// 解の計算に効く側 (要素の長さ・M-φ の曲率・回転ばねのモーメント) は解析の結果を丸めの単位で動かしうるので、
/// ここでは対象にしない (それぞれの場所で別に扱う)。
/// </summary>
[TestClass]
public class StableNormTests
{
    [TestMethod]
    public void Norm2_IsExactOnOrdinaryValues_AndDoesNotOverflowOrUnderflow()
    {
        Assert.AreEqual(5.0, StableNumerics.Norm(3, 4), 1e-15);
        Assert.AreEqual(5e200, StableNumerics.Norm(3e200, 4e200), 5e200 * 1e-15, "二乗の途中で無限大になっている");
        Assert.AreEqual(5e-200, StableNumerics.Norm(3e-200, 4e-200), 5e-200 * 1e-15, "二乗の途中で 0 に潰れている");
        Assert.IsTrue(double.IsNaN(StableNumerics.Norm(double.NaN, 1)));
        Assert.IsTrue(double.IsPositiveInfinity(StableNumerics.Norm(double.NegativeInfinity, 1)));
    }

    [TestMethod]
    public void Norm3_IsExactOnOrdinaryValues_AndDoesNotOverflowOrUnderflow()
    {
        Assert.AreEqual(3.0, StableNumerics.Norm(1, 2, 2), 1e-15);
        Assert.AreEqual(3e200, StableNumerics.Norm(1e200, -2e200, 2e200), 3e200 * 1e-15);
        Assert.AreEqual(3e-200, StableNumerics.Norm(1e-200, 2e-200, -2e-200), 3e-200 * 1e-15);
        Assert.AreEqual(0.0, StableNumerics.Norm(0, 0, 0));
        Assert.IsTrue(double.IsNaN(StableNumerics.Norm(1, double.NaN, double.PositiveInfinity)), "NaN を無限大に見せている");
        Assert.IsTrue(double.IsPositiveInfinity(StableNumerics.Norm(1, double.NegativeInfinity, 2)));
    }

    /// <summary>
    /// 結果・表示・計算書・検査の側に、二乗してから足す形が戻っていないこと。
    /// </summary>
    [TestMethod]
    public void TheDisplaySideUsesTheStableNorm()
    {
        const string e = @"[A-Za-z_]\w*(?:\.[A-Za-z_]\w*|\[[^\[\]]+\])*";
        var squared = new Regex(@"Math\.Sqrt\(\s*(" + e + @")\s*\*\s*\1\s*\+");
        var files = new[] { "Views", "Output", "Services", Path.Combine("Models", "Results") }
            .SelectMany(d => Directory.GetFiles(TestSource.Dir("Graphics_r1", d), "*.cs", SearchOption.AllDirectories))
            .Concat(new[] { "NodeLoad.cs", "NodeDisp.cs", "BeamForce.cs", "BeamDisp.cs" }
                .Select(f => Path.Combine(TestSource.Dir("Graphics_r1", "FEM"), f)))
            .ToList();
        TestSource.AssertScanned(files.Count, 100, "結果・表示・計算書・検査の側のソース");
        var hits = files.SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Path.GetFileName(f), Line: i + 1, Text: l)))
            .Where(l => !l.Text.TrimStart().StartsWith("//") && squared.IsMatch(l.Text))
            .Select(l => $"{l.File}:{l.Line}  {l.Text.Trim()}")
            .ToList();
        Assert.AreEqual(0, hits.Count, "二乗してから足す形が残っています。StableNumerics.Norm を使ってください:\n  "
            + string.Join("\n  ", hits));
    }

    /// <summary>モデルの検査の基礎梁の長さも同じ計算にそろえた (大きな座標差で無限大にならない)。</summary>
    [TestMethod]
    public void BeamLengthCheck_UsesTheStableNorm()
    {
        string src = TestSource.Read("Graphics_r1", "Services", "ModelConnectivityCheck.cs");
        StringAssert.Contains(src, "double length = PileDesign.Common.StableNumerics.Norm(dx, dy, dz);");
    }

    // ── 杭位置の重なりの警告 (座標を mm の整数に丸めて比べる) ──

    [TestMethod]
    public void PositionKey_RejectsCoordinatesThatCannotBeRounded()
    {
        Assert.IsTrue(ModelConnectivityCheck.TryPositionKey(1.2344, -5, out var key));
        Assert.AreEqual((1234L, -5000L), key);
        Assert.IsFalse(ModelConnectivityCheck.TryPositionKey(double.NaN, 0, out _));
        Assert.IsFalse(ModelConnectivityCheck.TryPositionKey(0, double.PositiveInfinity, out _));
        Assert.IsFalse(ModelConnectivityCheck.TryPositionKey(1e17, 0, out _), "long に収まらない座標を丸めている");
    }

    /// <summary>
    /// <b>本題。</b> 丸められない座標の杭どうしを「同じ位置」と言わないこと。
    /// 以前は範囲外の double を long にしていて、値が決まらず、離れた杭が同じ位置になり得た。
    /// </summary>
    [TestMethod]
    public void HugeCoordinates_AreReported_NotTreatedAsTheSamePosition()
    {
        var input = new PileDesign.Models.InputData.InputModel();
        input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
        input.PileLayoutItems ??= [];
        input.PileLayoutItems.Add(new PileDesign.Models.InputData.PileLayoutDataItem { No = 1, PileNo = 1, X = 1e17, Y = 0 });
        input.PileLayoutItems.Add(new PileDesign.Models.InputData.PileLayoutDataItem { No = 2, PileNo = 2, X = -3e17, Y = 0 });

        var warnings = ModelConnectivityCheck.CollectWarnings(input);

        Assert.IsFalse(warnings.Any(w => w.Contains("同じ位置")), "離れた杭を同じ位置としている");
        Assert.AreEqual(2, warnings.Count(w => w.Contains("重なりを判定できません")));
    }
}

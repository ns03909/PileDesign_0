using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;

namespace TestProject1;

/// <summary>
/// 解析結果の表で、大きな有限の値・数値でない値 (NaN・無限大) を普通の値に見せないこと。
///
/// <list type="bullet">
/// <item>合成値 √(x²+y²) (<see cref="AnalysisResultTableService.Resultant"/>): 二乗の途中で無限大になっていた。</item>
/// <item>ばね反力の合計 (<see cref="AnalysisResultTableService.SumSpringForces"/>):
/// 合計が無限大・NaN でも普通の行として並んでいた。</item>
/// </list>
/// </summary>
[TestClass]
public class ResultTableNumericTests
{
    // ── 表の合成値 ─────────────────────────────

    /// <summary>大きな有限の値でも無限大にならないこと。数値でない成分は数値でない値のまま (隠さない)。</summary>
    [TestMethod]
    public void Resultant_DoesNotOverflow()
    {
        Assert.AreEqual(5.0, AnalysisResultTableService.Resultant(3, 4), 1e-12);
        Assert.AreEqual(5e200, AnalysisResultTableService.Resultant(3e200, 4e200), 5e200 * 1e-12, "二乗の途中で無限大になっている");
        Assert.IsTrue(double.IsNaN(AnalysisResultTableService.Resultant(double.NaN, 1)));
    }

    // ── ばね反力の合計 ────────────────────────────

    private static (double, double, double)? F(double fx, double fy, double fz) => (fx, fy, fz);

    /// <summary>普通の合計は項目名に何も添えない。</summary>
    [TestMethod]
    public void SpringSum_Normal()
    {
        var row = AnalysisResultTableService.SumSpringForces("杭周地盤反力合計", [F(3, 4, 1), F(3, 4, 1)])!;
        Assert.AreEqual("杭周地盤反力合計", row.Item);
        Assert.AreEqual(6, row.Fx, 1e-12);
        Assert.AreEqual(10, row.Fh, 1e-12);
    }

    /// <summary>結果が 1 本も無ければ行を作らない。欠けていれば本数を添える。</summary>
    [TestMethod]
    public void SpringSum_Missing()
    {
        Assert.IsNull(AnalysisResultTableService.SumSpringForces("合計", [null, null]));
        var row = AnalysisResultTableService.SumSpringForces("合計", [F(1, 0, 0), null])!;
        StringAssert.Contains(row.Item, "1/2 本");
    }

    /// <summary><b>本題。</b> 数値でない反力を含む・合計が範囲を超えた行は、項目名でそう示す (普通の行に見せない)。</summary>
    [TestMethod]
    public void SpringSum_NonFinite_IsMarked()
    {
        var nan = AnalysisResultTableService.SumSpringForces("合計", [F(1, 0, 0), F(double.NaN, 0, 0)])!;
        StringAssert.Contains(nan.Item, "数値でない反力を含むばね 1 本");

        var overflow = AnalysisResultTableService.SumSpringForces("合計", [F(1e308, 0, 0), F(1e308, 0, 0)])!;
        StringAssert.Contains(overflow.Item, "合計が数値の範囲を超えています");
        Assert.IsFalse(double.IsFinite(overflow.Fx));
    }
}

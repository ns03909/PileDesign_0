using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System.Linq;

namespace TestProject1;

/// <summary>
/// 単位転倒モーメントに対する反力 (<see cref="InputModel.TryGetReactionForUnitMoment"/>) が、
/// 大きな有限の値・数値でない値 (NaN・無限大) で黙って誤った値を返さないこと。
///
/// 腕の長さの二乗をそのまま足していたので、座標が大きいと無限大になって反力がすべて 0 になり、
/// 「杭間隔がない」と誤った理由で止まった。数値でない座標では数値でない反力を返していた。
/// </summary>
[TestClass]
public class UnitMomentReactionTests
{
    private static InputModel WithPiles(params (double X, double Y)[] positions)
    {
        var input = new InputModel();
        input.AttachViewModel(new PileDesign.ViewModels.MainWindowViewModel { CurrentInputModel = input });
        input.PileLayoutItems ??= [];
        int no = 0;
        foreach (var (x, y) in positions)
        {
            no++;
            input.PileLayoutItems.Add(new PileLayoutDataItem { PileNo = no, No = no, X = x, Y = y });
        }
        return input;
    }

    // ── 単位転倒モーメントに対する反力 ─────────────────

    /// <summary>基準: 杭 2 本 (X = ±1) で X 方向なら、腕 ±1・Σ腕² = 2 → 反力 −0.5 / +0.5。</summary>
    [TestMethod]
    public void UnitMomentReaction_Basic()
    {
        var input = WithPiles((-1, 0), (1, 0));
        Assert.IsTrue(input.TryGetReactionForUnitMoment(0, out var r, out var problem), problem);
        Assert.AreEqual(-0.5, r[0], 1e-12);
        Assert.AreEqual(0.5, r[1], 1e-12);
    }

    /// <summary>
    /// <b>本題。</b> 座標が大きくても (二乗が倍精度の範囲を超える 1e200 m) 反力が正しいこと。
    /// 反力は 1/腕 に比例するので、座標を k 倍すると反力は 1/k 倍になる。
    /// </summary>
    [TestMethod]
    public void UnitMomentReaction_LargeCoordinates_ScaleCorrectly()
    {
        const double k = 1e200;
        var input = WithPiles((-k, 0), (k, 0));
        Assert.IsTrue(input.TryGetReactionForUnitMoment(0, out var r, out var problem), problem);
        Assert.AreEqual(0.5 / k, r[1], 0.5 / k * 1e-12, "腕の二乗和が無限大になり反力が 0 になっている");
        Assert.AreEqual(-0.5 / k, r[0], 0.5 / k * 1e-12);
    }

    /// <summary>
    /// 数値でない座標は、どの杭かを示して止める (以前は数値でない反力を返していた)。
    /// 座標のセッターは数値でない値を拒むので、ここではフィールドに直接入れて守りの側だけを確かめる。
    /// </summary>
    [TestMethod]
    public void UnitMomentReaction_NonFiniteCoordinate_IsReported()
    {
        var field = typeof(InputNode).GetField("_x", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(field, "座標のフィールド名が変わった (テストの前提が崩れている)");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity })
        {
            var input = WithPiles((-1, 0), (0, 0), (1, 0));
            field!.SetValue(input.PileLayoutItems[1], bad);
            Assert.IsFalse(input.TryGetReactionForUnitMoment(0, out var r, out var problem));
            Assert.AreEqual(0, r.Count);
            StringAssert.Contains(problem, "杭No.2");
            Assert.AreEqual(0, input.GetReactionForUnitMoment(0).Count, "従来の入口も数値でない反力を返している");
        }
    }

    /// <summary>荷重方向が数値でなければ止める。</summary>
    [TestMethod]
    public void UnitMomentReaction_NonFiniteAngle_IsReported()
    {
        var input = WithPiles((-1, 0), (1, 0));
        Assert.IsFalse(input.TryGetReactionForUnitMoment(double.NaN, out _, out var problem));
        StringAssert.Contains(problem, "荷重方向");
    }

    /// <summary>腕の長さそのものが倍精度の範囲を超えるときは、範囲外として止める (無限大の反力を返さない)。</summary>
    [TestMethod]
    public void UnitMomentReaction_ArmBeyondRange_IsReported()
    {
        var input = WithPiles((-1.5e308, -1.5e308), (1.5e308, 1.5e308));
        Assert.IsFalse(input.TryGetReactionForUnitMoment(45, out var r, out var problem));
        Assert.AreEqual(0, r.Count);
        StringAssert.Contains(problem, "範囲");
    }

    /// <summary>その方向に腕が無い (杭が同じ位置・方向と直交に並ぶ) ときは 0 を返す (従来どおり。呼び出し側が止める)。</summary>
    [TestMethod]
    public void UnitMomentReaction_NoArm_ReturnsZeros()
    {
        var input = WithPiles((0, -1), (0, 1));
        Assert.IsTrue(input.TryGetReactionForUnitMoment(0, out var r, out _));
        Assert.IsTrue(r.All(v => v == 0));
    }
}

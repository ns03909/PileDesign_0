using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.FEM;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 解析に使った杭頭の M-θ と、いまの計算方法の照合 (PileTopCurveCheck) の境界 (2026-11-28 のレビューの指摘)。
    /// <list type="bullet">
    /// <item>曲線を持たない線形ばね (鋼管杭の鉄筋定着工法の Kθ) は剛性の値で比べる。以前は曲線がどちらも無ければ一致とし、
    /// 剛性が変わっても見逃した。</item>
    /// <item>照合できない控え (軸力を読めない等) が 1 件でもある杭体は照合済みにしない。以前は 1 件でも比べられれば照合済みとし、
    /// 旧版の結果への注記を省いた。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class PileTopCurveCheckTests
    {
        [TestMethod]
        [Timeout(600000)]
        public void LinearSprings_AreComparedByStiffness_AndUncheckedSnapshotsAreNotVerified()
        {
            MainWindowViewModel vm;
            try
            {
                vm = TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunExampleForViewModel("Example3_5", "PileExample3_5",
                    new TestProject1.ConvergenceRegression.HeadlessHorizontalRunner.RunOptions { Level1Steps = 4, Level2Steps = 8, Parallelism = 1 });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }
            var input = vm.ResultInputModel!;
            var model = vm.CurrentModel!;

            // 前提: 線形ばね (曲線なし・剛性あり) の控えがある
            var linear = model.RotationalSprings
                .SelectMany(s => s.CaseMThetaSnapshots.Select(kv => (Spring: s, kv.Key, Snap: kv.Value)))
                .Where(x => x.Snap.CurveXY == null && x.Snap.KthetaXY is > 0 && x.Snap.KthetaXY < 1e12
                            && !(x.Snap.SetupReason ?? "").StartsWith("Rigid", StringComparison.Ordinal))
                .ToList();
            TestSource.AssertScanned(linear.Count, 1, "線形の杭頭回転ばねの控え");
            int bodyNo = linear[0].Spring.FindPileLayout(input.PileLayoutItems)!.PileBodyNo;

            // 再解析していない控えは一致する (誤検出しない)
            var same = PileTopCurveCheck.Compare(input, model);
            Assert.IsTrue(same.Compared >= linear.Count, "(前提) 線形ばねの控えを比べていない");
            Assert.AreEqual(0, same.Differences.Count, string.Join(" / ", same.Differences.Select(d => d.Detail)));
            Assert.IsTrue(same.Verified(bodyNo));

            // 線形ばねの剛性が変わった (計算方法の変更) → 剛性の違いとして分かる
            var (spring, key, snap) = linear[0];
            spring.CaseMThetaSnapshots[key] = Copy(snap, kthetaXY: snap.KthetaXY * 1.1);
            var changed = PileTopCurveCheck.Compare(input, model);
            Assert.IsTrue(changed.Differences.Any(d => d.Detail.Contains("回転剛性", StringComparison.Ordinal)),
                "線形ばねの剛性の違いを見逃しています");
            Assert.IsFalse(changed.Verified(bodyNo));
            spring.CaseMThetaSnapshots[key] = snap;

            // 軸力を読めない控えが 1 件 → その杭体は照合済みにせず、注記で件数を書く
            spring.CaseMThetaSnapshots[key] = Copy(snap, axialN: 0.0, reason: "CombinedXY(線形)");
            var partly = PileTopCurveCheck.Compare(input, model);
            Assert.AreEqual(1, partly.Unchecked);
            Assert.AreEqual(0, partly.Differences.Count);
            Assert.IsFalse(partly.Verified(bodyNo), "照合できない控えがあるのに照合済みにしています");
            StringAssert.Contains(PileTopCurveCheck.Note(partly, true), "1 件は、軸力を読めない・数値が有限でない");
            spring.CaseMThetaSnapshots[key] = snap;

            // 剛性が NaN の控え → 差の比較が偽になって一致と見なさず、照合できない控えとして数える
            spring.CaseMThetaSnapshots[key] = Copy(snap, kthetaXY: double.NaN);
            var nan = PileTopCurveCheck.Compare(input, model);
            Assert.AreEqual(1, nan.Unchecked, "非有限値の控えを照合できないと数えていません");
            Assert.IsFalse(nan.Verified(bodyNo), "非有限値の控えがあるのに照合済みにしています");
            spring.CaseMThetaSnapshots[key] = snap;
        }

        /// <summary>曲線の点・値に NaN・無限大があれば一致としない (NaN との比較は偽なので、差を > で見ると素通りした)。</summary>
        [TestMethod]
        public void NonFiniteValues_AreNeverTheSame()
        {
            var a = new[] { (0.0, 0.0), (0.001, 100.0), (0.01, 200.0) };
            var b = new[] { (0.0, 0.0), (0.001, double.NaN), (0.01, 500.0) };
            Assert.IsTrue(PileTopCurveCheck.SameCurve(a, a));
            Assert.IsFalse(PileTopCurveCheck.SameCurve(a, b));
            Assert.IsFalse(PileTopCurveCheck.SameCurve(b, b));
            Assert.IsFalse(PileTopCurveCheck.SameCurve(a, new[] { (0.0, 0.0), (0.001, 100.0), (double.PositiveInfinity, 200.0) }));
            Assert.IsFalse(PileTopCurveCheck.SameValue(double.NaN, double.NaN));
            Assert.IsFalse(PileTopCurveCheck.SameValue(1.0, double.PositiveInfinity));
            Assert.IsTrue(PileTopCurveCheck.SameValue(null, null));

            var snap = new MThetaCaseSnapshot
            {
                Mode = RotationalSpringMode.CombinedXY,
                CurveXY = new MomentRotationCurve(b),
            };
            Assert.IsFalse(PileTopCurveCheck.IsFinite(snap));
            Assert.IsTrue(PileTopCurveCheck.IsFinite(new MThetaCaseSnapshot { Mode = RotationalSpringMode.CombinedXY, KthetaXY = 1e5 }));
        }

        private static MThetaCaseSnapshot Copy(MThetaCaseSnapshot s, double? kthetaXY = null, double? axialN = null, string? reason = null) => new()
        {
            Mode = s.Mode, CurveXY = s.CurveXY, Curve = s.Curve, Ktheta = s.Ktheta, McrXY = s.McrXY,
            KthetaXY = kthetaXY ?? s.KthetaXY,
            AxialN = axialN ?? s.AxialN,
            SetupReason = reason ?? s.SetupReason,
        };
    }
}

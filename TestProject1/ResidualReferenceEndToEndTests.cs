using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using TestProject1.ConvergenceRegression;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 収束判定の基準値 (<see cref="ResidualReferenceMode"/>) を選んだとき、<b>実際に解析が通ること</b>。
    ///
    /// <para>純関数の検査 (<see cref="ResidualReferenceModeTests"/>) は比の作り方しか見ない。
    /// 基準値を変えると反復を止める位置が変わるので、選んだ設定で解析が収束するかは
    /// 例題を 1 本走らせて確かめる。基準値が入力から解析へ届いていない (既定に戻る) 形も、
    /// ここで一緒に捕まえる。</para>
    ///
    /// <para>設計例集3.1 を使う。液状化ケースを含み地盤変位が強制変位として入るので、
    /// 「外力だけでは基準が足りない」条件がそろっている。</para>
    /// </summary>
    [TestClass]
    public class ResidualReferenceEndToEndTests
    {
        [DataTestMethod]
        [DataRow(ResidualReferenceMode.ExternalForce)]
        [DataRow(ResidualReferenceMode.ExternalForceOrReaction)]
        [DataRow(ResidualReferenceMode.InternalForce)]
        public void EveryReferenceModeConvergesOnARealExample(ResidualReferenceMode mode)
        {
            var snapshot = HeadlessHorizontalRunner.RunExample("Example3_1", "PileExample3_1",
                new HeadlessHorizontalRunner.RunOptions
                {
                    Customize = model => model.FundamentalInput.ResidualReference = mode,
                });

            Assert.IsTrue(snapshot.Cases.Count > 0,
                $"{mode}: 解析ケースが 0 件です (基準値の切替で解析が組めなくなっていないか)");

            var unconverged = snapshot.Cases.Where(c => !c.Converged).Select(c => c.CaseKey).ToList();
            Assert.AreEqual(0, unconverged.Count,
                $"{mode}: 収束しないケースがあります: {string.Join(" / ", unconverged)}");

            int relaxed = snapshot.Cases.Sum(c => c.RelaxedSteps);
            Assert.AreEqual(0, relaxed,
                $"{mode}: 緩めた基準で受理したステップが {relaxed} 件あります");
        }

        /// <summary>
        /// <b>慣性力が 0 の組合せ</b>で、従来の基準では解析が始まらず、新しい基準では収束すること。
        ///
        /// <para>これがオプションを入れた理由そのもので、同時に「設定が入口から解法まで届いているか」の
        /// もっとも強い確認になる。地盤変位 (αL) だけで駆動し、βU = βL = 0 で慣性力を消す。</para>
        ///
        /// <para>従来の基準では分母が 0 になって判定が成り立たないので、解析前の入力検査
        /// (<c>CheckInputData.CheckLoadCombinations</c>) が<b>解析を始めさせない</b>。
        /// 新しい基準では検査を通り、反力を基準にして収束する。</para>
        /// </summary>
        [TestMethod]
        public void WithoutInertiaOnlyTheNewReferenceGetsAnAnswer()
        {
            static void KillInertia(InputModel model)
            {
                // αL = 1 で地盤変位は残し、βU = βL = 0 で慣性力だけ消す
                model.LoadCasesInput.LoadCombinations =
                    new System.Collections.ObjectModel.ObservableCollection<LoadCombination>
                    {
                        new LoadCombination(1, 1.0, 0.0, 0.0) { IsApplicable = true },
                    };
            }

            var oldWay = HeadlessHorizontalRunner.RunExample("Example3_1", "PileExample3_1",
                new HeadlessHorizontalRunner.RunOptions
                {
                    Customize = model =>
                    {
                        model.FundamentalInput.ResidualReference = ResidualReferenceMode.ExternalForce;
                        KillInertia(model);
                    },
                });

            Assert.AreEqual(0, oldWay.Cases.Count,
                "外力のみの基準では、慣性力 0 の組合せは入力検査で止まるはずです。"
                + "結果が出ているなら、収束判定が成り立たないまま解析が走っています");

            var newWay = HeadlessHorizontalRunner.RunExample("Example3_1", "PileExample3_1",
                new HeadlessHorizontalRunner.RunOptions
                {
                    Customize = model =>
                    {
                        model.FundamentalInput.ResidualReference = ResidualReferenceMode.ExternalForceOrReaction;
                        KillInertia(model);
                    },
                });

            Assert.IsTrue(newWay.Cases.Count > 0,
                "強制変位の反力を基準にすれば、慣性力 0 の組合せでも解析できるはずです "
                + "(入力検査が基準値の設定を見ていない可能性)");

            var unconverged = newWay.Cases.Where(c => !c.Converged).Select(c => c.CaseKey).ToList();
            Assert.AreEqual(0, unconverged.Count,
                "反力を基準にしたのに収束しないケースがあります: " + string.Join(" / ", unconverged));
        }

        /// <summary>
        /// 基準値を変えても、通常のケース (慣性力で駆動するケース) の答えが大きく動かないこと。
        ///
        /// <para>基準値を変えると反復を止める位置が変わりうるので、結果は厳密には一致しない。
        /// ただし許容値は同じ 1e-6 で、収束の近くでは残差が桁で落ちるため、実測では
        /// <b>設計例集3.1 は外力基準と内力平均基準で反復 69・最大水平変位 0.018065 m が一致する</b>
        /// (2026-09-12)。基準値の効き目が出るのは、外力が小さい・0 のケースである。</para>
        ///
        /// <para>ここは「基準値を変えたら別物になった」を弾く網。一致することは要求しない
        /// (一致は実装の都合で変わりうる)。</para>
        /// </summary>
        [TestMethod]
        public void ChangingTheReferenceMovesTheAnswerButOnlySlightly()
        {
            double Baseline(ResidualReferenceMode mode)
            {
                var snapshot = HeadlessHorizontalRunner.RunExample("Example3_1", "PileExample3_1",
                    new HeadlessHorizontalRunner.RunOptions
                    {
                        Customize = model => model.FundamentalInput.ResidualReference = mode,
                    });
                double disp = snapshot.Cases.Max(c => System.Math.Abs(c.MaxAbsHorizDisp));
                // 基準値の違いが反復数と応答にどう出るかを残す (失敗時の手掛かり・報告用)
                System.Console.WriteLine(
                    $"[基準={ResidualReferenceModes.ToShortText(mode)}] "
                    + $"反復 {snapshot.Cases.Sum(c => c.TotalIterations)}、"
                    + $"最大水平変位 {disp:F6} m、"
                    + $"最終残差 {snapshot.Cases.Max(c => c.FinalResidual):E2}");
                return disp;
            }

            double external = Baseline(ResidualReferenceMode.ExternalForce);
            double internalForce = Baseline(ResidualReferenceMode.InternalForce);

            Assert.IsTrue(external > 0, "代表の変位が 0 です (例題が走っていません)");

            double diff = System.Math.Abs(internalForce - external) / external;
            Assert.IsTrue(diff < 0.10,
                $"基準値を変えただけで最大水平変位が {diff * 100:F1}% 動きました。"
                + "許容値は同じなので、この差は収束判定の位置の違いで説明できる範囲に収まるはず");
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 解析の<b>出口側</b>の不変条件。
    ///
    /// これまでの網は入力の往復 (保存・複製・並び順) に閉じていて、解析結果については
    /// 収束スナップショットの回帰しか無かった。回帰は「前と違う」を捕まえるが、
    /// <b>最初から間違っていたもの</b>は捕まえられない。
    ///
    /// そこで、正解の値を用意せずに済む関係だけを見る。入力に変換をかけて、
    /// 出力がその変換に従うかを確かめる形にすれば、期待値表が要らない。
    ///
    /// <list type="bullet">
    /// <item>荷重の符号を反転したら応答も反転 (線形域に限る)</item>
    /// <item>荷重を 2 倍にしたら応答も 2 倍 (線形域に限る)</item>
    /// </list>
    ///
    /// 「荷重を 0 にしたら応答も 0」は使えない。<b>荷重が 0 のケースは意図的に
    /// スキップされる</b>ので (HorizontalCalculationViewModel.Run.cs、ログ付き)、
    /// 比べる相手が無くなる。
    ///
    /// 線形域に限るものは、杭体と地盤の非線形を切ってから確かめる。非線形が入ると
    /// 比例も反転も成り立たないのが<b>正しい</b>ので、切り忘れると意味の無い網になる。
    /// </summary>
    [TestClass]
    public class AnalysisOutputInvariantTests
    {
        /// <summary>
        /// 非線形を切って、純粋な線形の骨組にする。
        ///
        /// 地盤は<b>モードを直接 Linear にする</b>。旧 API の <c>IsSoilNonLinear = false</c>
        /// でも Linear に写るが、かつて「この bool は剛性に効いていなかった」時期があり、
        /// 3 段階のモードに置き換わった経緯がある。意図をモードで書いておく。
        /// </summary>
        private static void MakeLinear(InputModel model)
        {
            if (model.LoadCasesInput == null) return;
            foreach (var lc in model.LoadCasesInput.AllLoadCases)
            {
                lc.IsPileNonLinear = false;
                lc.SoilNonlinearityMode = SoilNonlinearityMode.Linear;
            }
        }

        private static void ScaleLoads(InputModel model, double factor)
        {
            if (model.LoadCasesInput == null) return;
            foreach (var lc in model.LoadCasesInput.AllLoadCases)
            {
                lc.UpperMassForce *= factor;
                lc.FoundationMassForce *= factor;
            }
        }

        /// <summary>水平荷重の向きを反転する。組合せ係数 β1・β2 の符号を反転させる。</summary>
        private static void ReverseHorizontalLoad(InputModel model)
        {
            if (model.LoadCasesInput?.LoadCombinations == null) return;
            foreach (var combo in model.LoadCasesInput.LoadCombinations)
            {
                combo.Beta1 = -combo.Beta1;
                combo.Beta2 = -combo.Beta2;
            }
        }

        private static HeadlessHorizontalRunner.RunOptions LinearOptions(
            Action<InputModel>? extra = null) => new()
            {
                Level1Steps = 2,
                Level2Steps = 2,
                // 液状化を入れると地盤変位が杭を押すので、荷重 0 でも応答が出る。
                // ここで見たいのは慣性力に対する応答なので切る。
                LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
                UseLineSearch = true,
                Parallelism = 1,
                ForceNonLinear = false,
                Customize = m =>
                {
                    MakeLinear(m);
                    extra?.Invoke(m);
                },
            };

        /// <summary>
        /// 線形域で、荷重の向きを反転したら応答も反転すること。
        ///
        /// 反転はアプリ自身の組合せ係数 (β1・β2) の符号で行う。荷重を 0 にする形は
        /// 使えない — <b>荷重が 0 のケースは意図的にスキップされる</b>ので
        /// (HorizontalCalculationViewModel.Run.cs、ログ付き)、比べる相手が無くなる。
        ///
        /// 反転しなければ、荷重に依らない項が混ざっているか、材料則・ばねの
        /// どこかが片側だけで定義されている。実際に 2026-04 に、M-θ が常に正の M を返し、
        /// M-φ の負側が永遠に降伏しないという<b>非対称</b>が見つかっている
        /// (counter-loading の収束不良の真因)。線形域なら、そこは奇関数でなければならない。
        ///
        /// <b>非線形は切ってから</b>見る。非線形が入ると反転が成り立たないのが正しい。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        public void InTheLinearRange_ReversingTheLoad_ReversesTheResponse(string groundName, string pileName)
        {
            ConvergenceSnapshot forward, reversed;
            try
            {
                forward = HeadlessHorizontalRunner.RunExample(groundName, pileName, LinearOptions());
                reversed = HeadlessHorizontalRunner.RunExample(
                    groundName, pileName, LinearOptions(m => ReverseHorizontalLoad(m)));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            Assert.AreEqual(forward.Cases.Count, reversed.Cases.Count,
                $"[{groundName}] ケース数が違います");
            Assert.IsTrue(forward.Cases.Count > 0, $"[{groundName}] 解析ケースが 0 件です");

            // 符号付きの量で見る。MaxAbsHorizDisp は絶対値なので反転を検出できない
            double scale = forward.Cases.Max(c =>
                Math.Max(Math.Abs(c.ApUx), Math.Abs(c.ApUy)));
            Assert.IsTrue(scale > 1e-6,
                $"[{groundName}] 代表節点の水平変位がほぼ 0 ({scale:E3}) です。反転を確かめられません");

            var offenders = new System.Collections.Generic.List<string>();
            int compared = 0;
            for (int i = 0; i < forward.Cases.Count; i++)
            {
                var f = forward.Cases[i];
                var r = reversed.Cases[i];
                foreach (var (name, fv, rv) in new[]
                {
                    ("apUx", f.ApUx, r.ApUx),
                    ("apUy", f.ApUy, r.ApUy),
                    ("apRz", f.ApRz, r.ApRz),
                })
                {
                    if (Math.Abs(fv) < scale * 1e-3) continue;   // ほぼ 0 の成分は見ない
                    compared++;
                    if (Math.Abs(rv + fv) > Math.Abs(fv) * 0.02) // 2% 許容 (反復の打ち切り誤差)
                        offenders.Add($"{f.CaseKey} {name}: 正 {fv:E4} / 逆 {rv:E4} (和 {fv + rv:E4})");
                }
            }

            // 「ほぼ 0 の成分は見ない」で全部落ちていたら、何も検査できていない
            TestSource.AssertScanned(compared, 1, $"{groundName} で比べた反転の成分");

            Assert.AreEqual(0, offenders.Count,
                $"[{groundName}] 線形域なのに荷重の反転で応答が反転しません。"
                + "荷重に依らない項が混ざっているか、材料則・ばねが片側だけで定義されています:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>
        /// 線形域で、荷重を 2 倍にしたら応答も 2 倍になること。
        ///
        /// 比例しなければ、どこかに荷重に依らない項が混ざっているか、荷重の載せ方が
        /// ステップ数に依存している。<b>非線形は切ってから</b>見る。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        public void InTheLinearRange_DoublingTheLoad_DoublesTheResponse(string groundName, string pileName)
        {
            ConvergenceSnapshot base1, doubled;
            try
            {
                base1 = HeadlessHorizontalRunner.RunExample(groundName, pileName, LinearOptions());
                doubled = HeadlessHorizontalRunner.RunExample(
                    groundName, pileName, LinearOptions(m => ScaleLoads(m, 2.0)));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            Assert.AreEqual(base1.Cases.Count, doubled.Cases.Count,
                $"[{groundName}] ケース数が違います");
            Assert.IsTrue(base1.Cases.Count > 0, $"[{groundName}] 解析ケースが 0 件です");

            // 応答が出ていること。0 と 0 を比べて合格しないように
            double maxBase = base1.Cases.Max(c => Math.Abs(c.MaxAbsHorizDisp));
            Assert.IsTrue(maxBase > 1e-6,
                $"[{groundName}] 元の応答がほぼ 0 ({maxBase:E3}) です。比例を確かめられません");

            var offenders = new System.Collections.Generic.List<string>();
            int compared = 0;
            for (int i = 0; i < base1.Cases.Count; i++)
            {
                double b = base1.Cases[i].MaxAbsHorizDisp;
                double d = doubled.Cases[i].MaxAbsHorizDisp;
                if (Math.Abs(b) < 1e-9) continue;

                compared++;
                double ratio = d / b;
                if (Math.Abs(ratio - 2.0) > 0.02)   // 2% 許容 (反復の打ち切り誤差)
                    offenders.Add($"{base1.Cases[i].CaseKey}: {b:E4} → {d:E4} (倍率 {ratio:F4})");
            }

            TestSource.AssertScanned(compared, 1, $"{groundName} で比べた比例のケース");

            Assert.AreEqual(0, offenders.Count,
                $"[{groundName}] 線形域なのに荷重の 2 倍が応答の 2 倍になりません。"
                + "荷重に依らない項が混ざっているか、荷重の載せ方がステップ数に依存しています:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }
    }
}

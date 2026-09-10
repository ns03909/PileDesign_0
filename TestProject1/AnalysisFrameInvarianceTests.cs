using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using TestProject1.ConvergenceRegression;

namespace TestProject1
{
    /// <summary>
    /// 座標系の取り方を変えても解析結果が変わらないこと。
    ///
    /// 地盤は水平成層で、水平方向には一様。だからモデル全体を<b>平行移動</b>しても
    /// 応答は変わってはならない。<b>鏡映</b>すれば応答も鏡映になる。
    /// どちらも正解の値を用意せずに確かめられる。
    ///
    /// この軸が要るのは、絶対座標に依存してはいけない処理が依存する形の不具合を
    /// 回帰スナップショットでは捕まえられないため。同じ座標どうしを比べているので、
    /// 座標をずらしたときに崩れることは分からない。
    ///
    /// 実際にこの種の不具合が起きている。土質杭の引き当ての表は
    /// <c>(地盤番号, 杭体番号, z)</c> を鍵にしており、しかもプログラム全体で 1 つしか
    /// 無かったため、別の入力モデルの値が返っていた (2026-09-10 に修正)。
    /// 座標を鍵にする処理は、座標をずらすと壊れやすい。
    /// </summary>
    [TestClass]
    public class AnalysisFrameInvarianceTests
    {
        private static void MakeLinear(InputModel model)
        {
            if (model.LoadCasesInput == null) return;
            foreach (var lc in model.LoadCasesInput.AllLoadCases)
            {
                lc.IsPileNonLinear = false;
                lc.SoilNonlinearityMode = SoilNonlinearityMode.Linear;
            }
        }

        private static HeadlessHorizontalRunner.RunOptions Options(Action<InputModel>? extra = null) => new()
        {
            Level1Steps = 2,
            Level2Steps = 2,
            // 地盤変位を入れない。液状化時の地盤変位は深さの関数なので、
            // 平行移動・鏡映の不変性とは別の話になる
            LiquefactionMode = HorizontalCalculationViewModel.LiquefactionOptionType.None,
            UseLineSearch = true,
            Parallelism = 1,
            ForceNonLinear = false,
            Customize = m => { MakeLinear(m); extra?.Invoke(m); },
        };

        /// <summary>モデル全体を平行移動する。杭の位置と荷重の作用点を同じだけ動かす。</summary>
        private static int Translate(InputModel model, double dx, double dy)
        {
            int moved = 0;

            foreach (var p in model.PileLayoutItems ?? [])
            {
                p.X += dx;
                p.Y += dy;
                moved++;
            }
            foreach (var lc in model.LoadCasesInput?.AllLoadCases ?? [])
            {
                lc.ForceActionPointX += dx;
                lc.ForceActionPointY += dy;
            }
            foreach (var n in model.InputNodes ?? [])
            {
                n.X += dx;
                n.Y += dy;
            }
            return moved;
        }

        /// <summary>X を反転する。杭の位置・荷重の作用点・荷重の向きをまとめて鏡映する。</summary>
        private static int MirrorX(InputModel model)
        {
            int moved = 0;

            foreach (var p in model.PileLayoutItems ?? [])
            {
                p.X = -p.X;
                moved++;
            }
            foreach (var lc in model.LoadCasesInput?.AllLoadCases ?? [])
            {
                lc.ForceActionPointX = -lc.ForceActionPointX;
                // 荷重の向きは (cosθ, sinθ)。X を反転すると (-cosθ, sinθ) = 180° - θ
                lc.LoadAngle = 180.0 - lc.LoadAngle;
            }
            foreach (var n in model.InputNodes ?? [])
            {
                n.X = -n.X;
            }
            return moved;
        }

        /// <summary>
        /// モデル全体を平行移動しても応答が変わらないこと。
        ///
        /// 地盤は水平成層なので、水平方向にどこへ置いても同じでなければならない。
        /// 変わるなら、絶対座標に依存してはいけない処理が依存している。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        public void TranslatingTheModel_DoesNotChangeTheResponse(string groundName, string pileName)
        {
            ConvergenceSnapshot home, moved;
            int piles = 0;
            try
            {
                home = HeadlessHorizontalRunner.RunExample(groundName, pileName, Options());
                moved = HeadlessHorizontalRunner.RunExample(groundName, pileName,
                    Options(m => { piles = Translate(m, 137.5, -82.25); }));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            TestSource.AssertScanned(piles, 1, $"{groundName} で動かした杭");
            AssertSameMagnitudes(home, moved, groundName,
                "平行移動しただけで応答が変わります。絶対座標に依存してはいけない処理が"
                + "依存しています (座標を鍵にした引き当て等)");
        }

        /// <summary>
        /// X を反転すると、応答の大きさは変わらず向きだけ反転すること。
        ///
        /// 地盤は水平方向に一様なので、鏡に映したモデルは鏡に映した応答になる。
        /// 大きさが変わるなら、左右で扱いが違う処理がある。
        /// </summary>
        [DataTestMethod]
        [DataRow("Example9", "PileExample9")]
        [DataRow("Example3_1", "PileExample3_1")]
        public void MirroringTheModel_MirrorsTheResponse(string groundName, string pileName)
        {
            ConvergenceSnapshot home, mirrored;
            int piles = 0;
            try
            {
                home = HeadlessHorizontalRunner.RunExample(groundName, pileName, Options());
                mirrored = HeadlessHorizontalRunner.RunExample(groundName, pileName,
                    Options(m => { piles = MirrorX(m); }));
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("例題ロード失敗"))
            {
                Assert.Inconclusive(ex.Message);
                return;
            }

            TestSource.AssertScanned(piles, 1, $"{groundName} で鏡映した杭");

            // 大きさは変わらない
            AssertSameMagnitudes(home, mirrored, groundName,
                "鏡映しただけで応答の大きさが変わります。左右で扱いが違う処理があります");

            // 代表節点の X 変位は符号が反転する
            int compared = 0;
            var offenders = new System.Collections.Generic.List<string>();
            for (int i = 0; i < home.Cases.Count; i++)
            {
                double a = home.Cases[i].ApUx;
                double b = mirrored.Cases[i].ApUx;
                if (Math.Abs(a) < 1e-9) continue;

                compared++;
                if (Math.Abs(b + a) > Math.Abs(a) * 0.02)
                    offenders.Add($"{home.Cases[i].CaseKey} apUx: 元 {a:E4} / 鏡映 {b:E4} (和 {a + b:E4})");
            }

            TestSource.AssertScanned(compared, 1, $"{groundName} で比べた鏡映の成分");
            Assert.AreEqual(0, offenders.Count,
                $"[{groundName}] 鏡映しても X 変位の符号が反転しません:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>ケースごとに応答の大きさが一致すること。</summary>
        private static void AssertSameMagnitudes(
            ConvergenceSnapshot a, ConvergenceSnapshot b, string groundName, string why)
        {
            Assert.AreEqual(a.Cases.Count, b.Cases.Count, $"[{groundName}] ケース数が違います");
            Assert.IsTrue(a.Cases.Count > 0, $"[{groundName}] 解析ケースが 0 件です");

            double scale = a.Cases.Max(c => Math.Abs(c.MaxAbsHorizDisp));
            Assert.IsTrue(scale > 1e-6,
                $"[{groundName}] 応答がほぼ 0 ({scale:E3}) です。不変性を確かめられません");

            var offenders = new System.Collections.Generic.List<string>();
            int compared = 0;
            for (int i = 0; i < a.Cases.Count; i++)
            {
                double x = a.Cases[i].MaxAbsHorizDisp;
                double y = b.Cases[i].MaxAbsHorizDisp;
                if (Math.Abs(x) < 1e-9) continue;

                compared++;
                double rel = Math.Abs(y - x) / Math.Abs(x);
                if (rel > 0.001)   // 0.1%。同じ問題を解いているので厳しく見る
                    offenders.Add($"{a.Cases[i].CaseKey}: 元 {x:E5} / 変換後 {y:E5} (差 {rel:P3})");
            }

            TestSource.AssertScanned(compared, 1, $"{groundName} で比べたケース");
            Assert.AreEqual(0, offenders.Count,
                $"[{groundName}] {why}:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }
    }
}

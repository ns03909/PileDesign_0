using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models;
using PileDesign.Models.InputData;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 杭頭部で入力した値が、半剛接合 3 工法 (キャプテン / FT-Pile / キャプリング) の
    /// 杭頭 M-θ に届くこと。パイルキャップの Fc・γ、FT-Pile の杭径、
    /// キャプリングの鋼管厚の 3 つを見る。
    ///
    /// <b>届いていませんでした。</b>3 つとも Fc・Ec をコンストラクタ引数でしか
    /// 受け取っておらず、どれも「まだ無ければ作る」形でしか生成されないので、
    /// 一度できたあとに入力を変えても反映されませんでした。FT-Pile に至っては
    /// 受け取った値を読むコードが無く、<c>Fc = 24.0</c> 固定でした。
    ///
    /// <b>既定が Fc=24・γ=23 で、FT-Pile の固定値もちょうどそれと同じ</b>だったため、
    /// 既定のままなら一致します。例題はすべて既定値なので、この不具合は
    /// 利用者が Fc を変えたときにだけ出ます。ゴールデンにも出ません。
    /// </summary>
    [TestClass]
    public class PileTopWiringTests
    {
        private const double DefaultFc = 24.0;
        private const double ChangedFc = 42.0;

        private static double EcOf(double fc, double gamma)
            => 3.35e4 * Math.Pow(gamma / 24.0, 2.0) * Math.Pow(fc / 60.0, 1.0 / 3.0);

        // ---- FT-Pile 構法 ----

        [TestMethod]
        public void FtPile_UsesTheEnteredFc_NotAFixed24()
        {
            var cap = new FTPileCap(ChangedFc, EcOf(ChangedFc, 23.0));

            Assert.AreEqual(ChangedFc, cap.Fc, 1e-9,
                "FT-Pile のパイルキャップ Fc が入力を見ていません。"
                + "曲げ耐力 3/5·φc·Ap·Fc がそのままずれます");
            Assert.AreEqual(EcOf(ChangedFc, 23.0), cap.E, 1e-6,
                "初期回転剛性 K0 は E に比例します");
        }

        [TestMethod]
        public void FtPile_TakesALaterChange()
        {
            var cap = new FTPileCap(DefaultFc, EcOf(DefaultFc, 23.0));
            Assert.AreEqual(DefaultFc, cap.Fc, 1e-9);

            Assert.IsTrue(cap.SetPileCapConcrete(ChangedFc, EcOf(ChangedFc, 23.0)),
                "変わったのに「変わっていない」と返しています");
            Assert.AreEqual(ChangedFc, cap.Fc, 1e-9);
        }

        [TestMethod]
        public void FtPile_IgnoresZero_SoTheStrengthNeverBecomesZero()
        {
            var cap = new FTPileCap(ChangedFc, EcOf(ChangedFc, 23.0));

            Assert.IsFalse(cap.SetPileCapConcrete(0.0, 0.0));
            Assert.AreEqual(ChangedFc, cap.Fc, 1e-9,
                "0 を受け入れると 3/5·φc·Ap·Fc が 0 になり、"
                + "θ 上限と η が 0 除算になります");

            Assert.IsFalse(cap.SetPileCapConcrete(-1.0, 100.0));
            Assert.AreEqual(ChangedFc, cap.Fc, 1e-9);
        }

        [TestMethod]
        public void FtPile_SayingNothingChanged_MeansNothingChanged()
        {
            var cap = new FTPileCap(ChangedFc, EcOf(ChangedFc, 23.0));
            Assert.IsFalse(cap.SetPileCapConcrete(ChangedFc, EcOf(ChangedFc, 23.0)),
                "同じ値なら false を返すこと。解析中は軸力ごとに呼ばれるので、"
                + "毎回 M-θ を組み直すと目に見えて遅くなります");
        }

        // ---- 3 工法をまとめて配る側 ----

        [TestMethod]
        public void ChangingFc_ReachesTheCapringPile()
        {
            var pileTop = new PileTop { CapringPile = new CapringPile(EcOf(DefaultFc, 23.0)) };
            pileTop.CapringPile.PileCapFc = DefaultFc;
            pileTop.CapringPile.PileCapEc = EcOf(DefaultFc, 23.0);

            pileTop.PileCapFc = ChangedFc;      // 杭頭部ウィンドウの入力欄に相当

            Assert.AreEqual(ChangedFc, pileTop.CapringPile.PileCapFc, 1e-9,
                "入力欄には新しい値が見えているのに、解析は古い値で走ります");
            Assert.AreEqual(pileTop.PileCapEc, pileTop.CapringPile.PileCapEc, 1e-6,
                "PileCapEc は Ec / Eb / Ep に入って杭頭ばねの剛性そのものになります");
        }

        [TestMethod]
        public void ChangingGamma_AlsoReaches_BecauseEcDependsOnIt()
        {
            var pileTop = new PileTop { CapringPile = new CapringPile(EcOf(DefaultFc, 23.0)) };
            pileTop.CapringPile.PileCapEc = EcOf(DefaultFc, 23.0);

            pileTop.PileCapGamma = 24.5;

            Assert.AreEqual(pileTop.PileCapEc, pileTop.CapringPile.PileCapEc, 1e-6,
                "Ec は γ² に比例します。γ だけ変えても剛性は動きます");
        }

        /// <summary>
        /// Ec は Fc・γ から毎回計算し直す getter なので、値そのものは常に正しい。
        /// ただし変更通知が飛ばないと、杭頭部ウィンドウの Ec 欄が既定
        /// (Fc=24 の 22,669) のまま残る。<b>計算は合っているのに表示だけ古い</b>
        /// という形なので、グラフを見ても気づけない。
        /// </summary>
        [TestMethod]
        public void ChangingFc_TellsTheScreenThatEcMoved()
        {
            var pileTop = new PileTop();
            var raised = new System.Collections.Generic.List<string?>();
            pileTop.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            pileTop.PileCapFc = 36.0;

            CollectionAssert.Contains(raised, nameof(PileTop.PileCapEc),
                "Fc を変えたのに Ec の変更通知が飛んでいません。"
                + "画面の Ec 欄が古い値のまま残ります");

            raised.Clear();
            pileTop.PileCapGamma = 24.5;
            CollectionAssert.Contains(raised, nameof(PileTop.PileCapEc),
                "Ec は γ² に比例します。γ を変えたときも知らせること");
        }

        [TestMethod]
        public void Ec_FollowsTheEnteredFc()
        {
            var pileTop = new PileTop { PileCapFc = 21.0 };
            Assert.AreEqual(EcOf(21.0, 23.0), pileTop.PileCapEc, 1.0);

            pileTop.PileCapFc = 36.0;
            Assert.AreEqual(EcOf(36.0, 23.0), pileTop.PileCapEc, 1.0);

            Assert.AreNotEqual(Math.Round(EcOf(24.0, 23.0)), Math.Round(pileTop.PileCapEc),
                "既定 (Fc=24) の 22,669 のままになっています");
        }

        [TestMethod]
        public void ApplyingIsSafe_WhenNoPileTopModelExists()
        {
            // 鉄筋定着工法など、3 工法のどれでもない場合
            var pileTop = new PileTop();
            pileTop.ApplyPileCapConcrete();      // 例外にならないこと
            pileTop.PileCapFc = ChangedFc;
            Assert.AreEqual(ChangedFc, pileTop.PileCapFc, 1e-9);
        }

        // ---- FT-Pile の杭径 ----

        [TestMethod]
        public void FtPile_TakesThePileDiameterFromTheSection()
        {
            var ft = new FTPile(DefaultFc, EcOf(DefaultFc, 23.0));

            ft.SetDimensionsFromSection(pileDiameter: 1000.0, concreteThickness: 100.0);

            Assert.AreEqual(1000.0, ft.FTPilePile.D1, 1e-9, "外径が杭断面と違います");
            Assert.AreEqual(800.0, ft.FTPilePile.D2, 1e-9, "内径 = 外径 − 2×コンクリート厚");
        }

        [TestMethod]
        public void FtPile_FallsBackToAHollowRatio_WhenTheThicknessIsUnknown()
        {
            var ft = new FTPile(DefaultFc, EcOf(DefaultFc, 23.0));

            ft.SetDimensionsFromSection(pileDiameter: 1000.0, concreteThickness: 0.0);

            Assert.AreEqual(600.0, ft.FTPilePile.D2, 1e-9,
                "コンクリート厚が入っていないときは中空比 0.6 で仮に置きます");
        }

        [TestMethod]
        public void FtPile_IgnoresAnEmptyDiameter()
        {
            var ft = new FTPile(DefaultFc, EcOf(DefaultFc, 23.0));
            ft.SetDimensionsFromSection(1000.0, 100.0);

            ft.SetDimensionsFromSection(0.0, 100.0);

            Assert.AreEqual(1000.0, ft.FTPilePile.D1, 1e-9,
                "杭径が未入力のときに 0 を入れると、Ap も K0 も 0 になります");
        }

        // ---- キャプリングの鋼管厚 ----

        [TestMethod]
        public void CapringPile_TakesThePipeThicknessFromTheSection()
        {
            var cp = new CapringPile(EcOf(DefaultFc, 23.0));

            Assert.IsTrue(cp.SetSteelPipeFromSection(isConcreteFilled: true, wallThickness: 12.0));
            Assert.IsTrue(cp.IsConcreteFilledSteelPipe);
            Assert.AreEqual(12.0, cp.SteelPipeWallThickness, 1e-9,
                "鋼管厚は合成 EI (Es·I_pipe + Ec·I_fill) に効きます");

            Assert.IsFalse(cp.SetSteelPipeFromSection(true, 12.0),
                "同じ値なら何もしないこと。解析中は軸力ごとに通ります");
        }

        [TestMethod]
        public void CapringPile_TurnsOffTheCompositeMode_WhenThePileIsNoLongerASteelPipe()
        {
            var cp = new CapringPile(EcOf(DefaultFc, 23.0));
            cp.SetSteelPipeFromSection(true, 12.0);

            Assert.IsTrue(cp.SetSteelPipeFromSection(isConcreteFilled: false, wallThickness: 0.0));
            Assert.IsFalse(cp.IsConcreteFilledSteelPipe,
                "杭体タイプを鋼管杭から変えたのに合成 EI のままだと、"
                + "存在しない鋼管の剛性で杭頭を計算します");
        }

        // ---- 計算書 ----

        /// <summary>
        /// パイルキャップの Fc・γ・Ec が計算書の杭頭諸元表に出ること。
        ///
        /// 半剛接合の 3 工法では、この 3 つが杭頭 M-θ を動かす (FT-Pile は
        /// 曲げ耐力 3/5·φc·Ap·Fc と K0、キャプテンは CTPConcrete、キャプリングは
        /// Ec/Eb/Ep)。それなのに諸元表に出ておらず、<b>計算書だけを見ても
        /// 検算できませんでした。</b>鉄筋定着工法の諸元には元から入っています。
        /// </summary>
        [TestMethod]
        public void TheReport_ShowsThePileCapConcrete()
        {
            var pileTop = new PileTop { PileCapFc = 36.0, PileCapGamma = 24.0 };

            var specs = pileTop.GetPileCapConcreteSpecs().ToList();

            CollectionAssert.AreEquivalent(
                new[] { "Fc", "γc", "Ec" }, specs.Select(s => s.Mark).ToArray(),
                "杭頭諸元表に出す 3 行が揃っていません");
            Assert.AreEqual("36", specs.First(s => s.Mark == "Fc").Value);
            Assert.AreEqual(pileTop.PileCapEc.ToString("N0"),
                specs.First(s => s.Mark == "Ec").Value,
                "Ec は Fc・γ から計算した値をそのまま出すこと");
        }

        /// <summary>
        /// 計算書の杭頭諸元表が、半剛接合の 3 工法すべてでパイルキャップの
        /// 諸元を前に付けること。工法を足したときに付け忘れると、その工法だけ
        /// 静かに欠ける。
        /// </summary>
        [TestMethod]
        public void TheReport_AddsItForEverySemiRigidMethod()
        {
            var body = TestSource.MethodBody(
                TestSource.Read("Graphics_r1", "Output", "WordDocument.PileDiagrams.cs"),
                "void AddPileTopSpecsTables(");

            Assert.IsTrue(body.Contains("GetPileCapConcreteSpecs"),
                "杭頭諸元表がパイルキャップの諸元を出していません");
            foreach (var method in new[] { "キャプテンパイル工法", "FT-Pile構法", "キャプリングパイル工法" })
            {
                Assert.IsTrue(body.Contains(method),
                    $"{method} が杭頭諸元表の対象から漏れています");
            }
        }

        // ---- 元に戻す / キャンセル ----

        /// <summary>
        /// 取り置いたスナップショットが、生きているモデルと中身を共有しないこと。
        /// <c>PileTop.DeepCopy()</c> は <c>ShallowCopy()</c> と 1 文字も違わなかったので、
        /// スナップショットを取ったあとに杭頭接合部をいじると<b>スナップショットも
        /// 一緒に変わり</b>、Ctrl+Z で戻せなかった。
        /// </summary>
        [TestMethod]
        public void TheSnapshot_DoesNotMoveWithTheLiveModel()
        {
            var live = new PileTop
            {
                PileCapFc = DefaultFc,
                FTPile = new FTPile(DefaultFc, EcOf(DefaultFc, 23.0)),
                CapringPile = new CapringPile(EcOf(DefaultFc, 23.0)),
                CaptainPile = new CaptainPile(),
            };
            live.FTPile.SetDimensionsFromSection(1000.0, 100.0);
            live.CapringPile.SetSteelPipeFromSection(true, 12.0);

            var snapshot = live.DeepCopy();

            // スナップショットを取ったあとに画面でいじる
            live.PileCapFc = ChangedFc;
            live.FTPile.SetDimensionsFromSection(600.0, 100.0);
            live.CapringPile.SetSteelPipeFromSection(true, 19.0);

            Assert.AreEqual(DefaultFc, snapshot.PileCapFc, 1e-9);
            Assert.AreEqual(1000.0, snapshot.FTPile.FTPilePile.D1, 1e-9,
                "FT-Pile の杭径がスナップショットでも動いています。元に戻せません");
            Assert.AreEqual(12.0, snapshot.CapringPile.SteelPipeWallThickness, 1e-9,
                "キャプリングの鋼管厚がスナップショットでも動いています");
            Assert.AreNotSame(live.FTPile, snapshot.FTPile);
            Assert.AreNotSame(live.CapringPile, snapshot.CapringPile);
            Assert.AreNotSame(live.CaptainPile, snapshot.CaptainPile);
        }

        /// <summary>
        /// 杭頭部ウィンドウの「キャンセル」と「元に戻す」が、
        /// ViewModel のプロパティだけでなく<b>モデルにも書き戻す</b>こと。
        ///
        /// この画面は <c>PileBodies[n].PileTop</c> を参照で受け取り、バインディングで
        /// 直接書き換える。ViewModel 側の参照を差し替えるだけではモデルに何も起きず、
        /// <b>キャンセルしても編集が残ったまま</b>だった。
        /// </summary>
        [TestMethod]
        public void CancelAndUndo_WriteBackToTheModel()
        {
            var source = TestSource.Read("Graphics_r1", "ViewModels", "PileTopViewModel.cs");

            int scanned = 0;
            var offenders = new System.Collections.Generic.List<string>();
            foreach (var name in new[] { "OnCancel", "Undo", "Redo" })
            {
                var body = TestSource.MethodBody(source, $"void {name}()");
                if (string.IsNullOrEmpty(body)) continue;
                scanned++;
                // 復元しているのに ApplyPileTop を通っていない
                bool restores = body.Contains("PrevPileTop") || body.Contains("CurrentState");
                if (restores && !body.Contains("ApplyPileTop"))
                    offenders.Add(name);
            }

            TestSource.AssertScanned(scanned, 3, "杭頭部ウィンドウの復元メソッド");
            Assert.AreEqual(0, offenders.Count,
                "ViewModel のプロパティを差し替えるだけでは、モデルは元に戻りません。"
                + "ApplyPileTop() を通してください: " + string.Join(", ", offenders));
        }

        // ---- 網 ----

        /// <summary>
        /// パイルキャップの Fc・Ec を、<see cref="PileTop.ApplyPileCapConcrete"/> を
        /// 通さずに杭頭モデルへ書き写さないこと。
        ///
        /// もとの不具合は、杭頭部ウィンドウのコンストラクタが
        /// <c>CapringPile.PileCapFc = PileTop.PileCapFc</c> と<b>直接</b>書き写して
        /// いたことでした。開いた瞬間にしか走らず、<c>Update()</c> も呼ばないので、
        /// 同期したつもりで剛性は古いまま、という形になります。
        /// </summary>
        [TestMethod]
        public void NobodyCopiesPileCapConcreteByHand()
        {
            var offenders = new System.Collections.Generic.List<string>();
            int scanned = 0;

            var dir = Path.Combine(TestSource.Root(), "Graphics_r1");
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                    continue;

                string name = Path.GetFileName(file);
                // 配る本人と、受け取る側の実装は当然書き込む
                if (name is "PileTop.cs" or "CapringPile.cs" or "CaptainPile.cs" or "FTPile.cs") continue;

                scanned++;
                var text = File.ReadAllText(file);
                foreach (Match m in Regex.Matches(
                             text, @"^.*\.(PileCapFc|PileCapEc)\s*=(?!=).*$", RegexOptions.Multiline))
                {
                    string line = m.Value.Trim();
                    if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                    // オブジェクト初期化子 (生成時に渡す形) は許す
                    if (Regex.IsMatch(line, @"^(PileCapFc|PileCapEc)\s*=")) continue;
                    offenders.Add($"{name}: {line}");
                }
            }

            TestSource.AssertScanned(scanned, 100, "Graphics_r1 のソース");
            Assert.AreEqual(0, offenders.Count,
                "パイルキャップの Fc / Ec を直接書き写しています。"
                + "PileTop.ApplyPileCapConcrete() を通してください "
                + "(通さないと Update() が走らず、剛性が古いまま残ります):"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }
    }
}

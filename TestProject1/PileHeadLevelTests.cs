using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 杭頭レベルが複数あるときの、杭体ウィンドウ・地盤の標高 (2026-10-23 の指摘「地盤と杭頭のレベル関係が適切でない」)。
    ///
    /// <list type="bullet">
    /// <item>杭体ウィンドウの支持力は、画面用の仮の土層-杭セットで求めていた。仮のセットは杭区間の境だけで区切り、
    /// 地層の境で区切らなかったので、区間の土層がその区間の下端の 1 層で決まった (例題10 で Rfu が解析の 10,074 kN に対し 29,158 kN)。</item>
    /// <item>杭体ウィンドウの「杭頭Z」は開くたびに 0 から始まり、杭配置の杭頭と結び付いていなかった (地盤を重ねた姿図で杭頭が地表にあるように描いた)。</item>
    /// <item>地盤の標高と GL 深さが食い違うと、杭のばね (標高) と地盤の計算 (GL 深さ) が別の地下水位で計算された。</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class PileHeadLevelTests
    {
        /// <summary>例題10 の杭の半分を 1.5 m 下げ、杭頭レベルを 2 つ (−2.0・−3.5) にする。</summary>
        internal static void TwoPileHeadLevels(InputModel model)
        {
            var piles = model.PileLayoutItems.ToList();
            for (int i = 0; i < piles.Count; i += 2) piles[i].Z -= 1.5;
            model.GenerateSoilPiles();
        }

        private static (MainWindowViewModel vm, InputModel input) BuildTwoLevels()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) throw new AssertInconclusiveException("例題ファイルなし: " + err);
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            TwoPileHeadLevels(input);
            return (vm, input);
        }

        private static void OnSta(Action action)
        {
            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                XamlSmokeTestSupport.EnsureApplicationResources();
                bool unattended = MessageService.IsUnattended;
                MessageService.IsUnattended = true;
                try { action(); }
                finally { MessageService.IsUnattended = unattended; }
            }, out bool timedOut);
            if (timedOut) throw new AssertInconclusiveException("STA スレッドが時間内に終わりませんでした");
            if (captured is AssertInconclusiveException inconclusive) throw inconclusive;
            if (captured != null) throw new AssertFailedException(captured.ToString());
        }

        [TestMethod]
        public void ThePileBodyWindow_ComputesTheSameSetAsTheAnalysis_AtEachPileHeadLevel()
        {
            OnSta(() =>
            {
                var (vm, input) = BuildTwoLevels();
                var sets = input.ElementDivision.SoilPiles.ToList();
                Assert.AreEqual(2, sets.Count, "杭頭レベル 2 つの土層-杭セットになっていません");

                var window = new PileBodyViewModel(vm);
                foreach (var sp in sets)
                {
                    var choice = window.PileHeadChoices.Single(c => c.GroundNo == sp.GroundNo
                        && Math.Abs(c.PileHeadZ - sp.Z) < 1e-9);
                    window.SelectedPileHeadChoice = choice;
                    var t = window.TemporarySoilPile;
                    CollectionAssert.AreEqual(sp.ZDataItems.Select(z => z.Z).ToArray(), t.ZDataItems.Select(z => z.Z).ToArray(),
                        $"杭頭Z {sp.Z:F2}: 杭体ウィンドウの節点が解析と違います (地層の境で区切っていない)");
                    Assert.AreEqual(sp.Rpu, t.Rpu, 1e-6, $"杭頭Z {sp.Z:F2}: 先端の支持力");
                    Assert.AreEqual(sp.Rfu, t.Rfu, 1e-6, $"杭頭Z {sp.Z:F2}: 周面摩擦力が解析と違います");
                    Assert.AreEqual(sp.R_ULS, t.R_ULS, 1e-6, $"杭頭Z {sp.Z:F2}: 終局の支持力が解析と違います");
                }
            });

            // 組み立ては 1 つ (写しを持たない)
            string window = TestSource.Read("Graphics_r1", "ViewModels", "PileBodyViewModel.cs");
            StringAssert.Contains(TestSource.MethodBody(window, "public void UpdateTemporarySoilPile("), "InputModel.SoilPileNodeAltitudes(");
            string model = TestSource.Read("Graphics_r1", "Models", "InputData", "InputModel.cs");
            StringAssert.Contains(TestSource.MethodBody(model, "public void GenerateSoilPiles("), "SoilPileNodeAltitudes(");
        }

        [TestMethod]
        public void ThePileBodyWindow_OffersThePileHeadLevelsOfTheLayout()
        {
            OnSta(() =>
            {
                var (vm, _) = BuildTwoLevels();
                var window = new PileBodyViewModel(vm);
                Assert.AreEqual(2, window.PileHeadChoices.Count, "杭頭レベル 2 つを選べません");
                Assert.IsTrue(window.PileHeadChoices.All(c => c.PileCount == 12), "組ごとの杭の本数が違います");

                // 開いたときは杭配置の杭頭 (上の方) を選んだ状態。以前は 0 から始まった
                Assert.IsNotNull(window.SelectedPileHeadChoice, "開いたときに杭配置の杭頭を選んでいません");
                Assert.AreEqual(-2.0, window.PileTopAltitude, 1e-9, "開いたときの杭頭Z が杭配置の杭頭ではありません");

                window.SelectedPileHeadChoice = window.PileHeadChoices[1];
                Assert.AreEqual(-3.5, window.PileTopAltitude, 1e-9, "選んだ杭頭レベルになっていません");
                Assert.AreEqual(1, window.SelectedGroundNo);

                // 手で打った杭頭Z が杭配置に無ければ選択は外れ、合えば選び直る
                window.PileTopAltitude = -2.75;
                Assert.IsNull(window.SelectedPileHeadChoice, "杭配置に無い杭頭Z なのに、組を選んだままです");
                window.PileTopAltitude = -2.0;
                Assert.AreSame(window.PileHeadChoices[0], window.SelectedPileHeadChoice);
                StringAssert.Contains(window.PileHeadChoices[0].Label, "-2.000", "杭頭Z の負の値が 3 桁で出ていません");
            });

            string xaml = TestSource.Read("Graphics_r1", "Views", "PileBodyWindow.xaml");
            StringAssert.Contains(xaml, "SelectedItem=\"{Binding SelectedPileHeadChoice}\"", "杭配置の組を選ぶ欄がありません");
            Assert.IsFalse(xaml.Contains("-0.00;"), "負の値だけ小数 2 桁で出す書式が残っています");
        }

        /// <summary>
        /// 杭の有効応力を杭ごとの基礎底 (杭頭の高さ) から数える選択 (2026-10-24)。選ぶと、杭頭レベルの違う土層-杭セットは
        /// 同じ標高でも違う有効応力 (塑性地盤反力 py) になり、深い段の杭の杭頭では土被りが 0 から始まる。選ばなければ今までどおり。
        /// </summary>
        [TestMethod]
        public void TheStressPlane_CanFollowEachPileHead()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();   // 地盤ウィンドウの VM が見ていない実体にする
            TwoPileHeadLevels(input);
            var ground = input.GroundAt(1)!;

            double PyAt(double z, bool deep)
            {
                var sp = input.ElementDivision.SoilPiles.Single(s => deep ? s.Z < -3 : s.Z > -3);
                int i = sp.ZDataItems.Select(d => d.Z).ToList().FindIndex(v => Math.Abs(v - z) < 1e-6);
                Assert.IsTrue(i >= 0, $"(前提) 標高 {z} の節点がありません");
                return sp.HorizontalSoilReactions[i].PyFrontTop;
            }
            // 砂質土 (シルト混じり砂2、-2.8〜-7.5) の下端。粘性土の py は有効応力によらないので砂で比べる
            double PySandBottom(bool deep)
            {
                var sp = input.ElementDivision.SoilPiles.Single(s => deep ? s.Z < -3 : s.Z > -3);
                int i = sp.ZDataItems.Select(d => d.Z).ToList().FindIndex(v => Math.Abs(v - (-7.5)) < 1e-6) - 1;
                Assert.IsTrue(i >= 0 && sp.HorizontalSoilReactions[i].SoilType == "砂質土", "(前提) -7.5 で終わる砂質土の要素がありません");
                return sp.HorizontalSoilReactions[i].PyFrontBtm;
            }

            Assert.IsFalse(ground.IsStressFromPileFoundationBottom, "既定は入力した地中応力計算用標高");
            Assert.AreEqual(PySandBottom(deep: false), PySandBottom(deep: true), 1e-9, "選ばないときは、同じ標高の py が杭頭レベルによらず同じ");
            double deepTopOff = PyAt(-3.5, deep: true);

            ground.IsStressFromPileFoundationBottom = true;
            input.GenerateSoilPiles();
            Assert.AreEqual(ground.StressTopLevelForPile(-3.5), -3.5, 1e-12);
            Assert.IsTrue(PySandBottom(deep: true) < PySandBottom(deep: false), "杭頭の高さから数えると、深い段の杭は同じ標高でも土被りが小さいはず");
            Assert.IsTrue(PyAt(-3.5, deep: true) < deepTopOff, "深い段の杭頭で、掘削した土の土被りを数えています");

            StringAssert.Contains(TestSource.Read("Graphics_r1", "Views", "GroundWindow.xaml"), "GroundInput.IsStressFromPileFoundationBottom");
        }

        /// <summary>
        /// 粘性土の py の深さ z は、有効応力を数える面と同じ面から測る (2026-10-24、利用者の判断)。
        /// 以前は有効応力を基礎底や地中応力計算用Z から数えても、粘性土だけ地表から測っていたので、
        /// 深い段の杭頭でも 9cu (深い側の式) になり、砂と粘土で「地表」の考え方が食い違った。
        /// </summary>
        [TestMethod]
        public void ClayDepth_IsMeasuredFromTheSamePlaneAsTheEffectiveStress()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();   // 地盤ウィンドウの VM が見ていない実体にする
            var ground = input.GroundAt(1)!;
            const double cu = 100.0;
            foreach (var layer in ground.GroundLayers) { layer.GranularityClass = "粘性土"; layer.Cohesive = cu; }
            TwoPileHeadLevels(input);

            HorizontalSoilReactionItem DeepHeadElement()
            {
                var sp = input.ElementDivision.SoilPiles.Single(s => s.Z < -3);
                int i = sp.ZDataItems.Select(d => d.Z).ToList().FindIndex(v => Math.Abs(v - (-3.5)) < 1e-6);
                Assert.IsTrue(i >= 0, "(前提) 深い段の杭頭 (-3.5) の節点がありません");
                return sp.HorizontalSoilReactions[i];
            }

            // 地中応力計算用Z = 地表のとき: 地表から測る (今までどおり)
            Assert.AreEqual(ground.GroundTopAltitude, ground.StressAltitude, 1e-9, "(前提) 例題の地中応力計算用Z は地表");
            var off = DeepHeadElement();
            double fromSurface = HorizontalSoilReactionItem.GetPy("粘性土", true, off.B, -3.5, 0.0, 0.0, cu, 0.0, ground.GroundTopAltitude);
            Assert.AreEqual(fromSurface, off.PyFrontTop, 1e-9, "地中応力計算用Z が地表なら、地表から測る");
            Assert.IsTrue(off.PyFrontTop > 2 * cu + 1, "(前提) 地表から測ると杭頭でも 2cu より大きい");

            // 杭ごとの基礎底から数える: 深い段の杭頭で深さ 0 → 2cu
            ground.IsStressFromPileFoundationBottom = true;
            input.GenerateSoilPiles();
            Assert.AreEqual(2 * cu, DeepHeadElement().PyFrontTop, 1e-9, "基礎底から数えるのに、粘性土の深さを地表から測っています");

            // 選ばないときも、地中応力計算用Z から測る
            ground.IsStressFromPileFoundationBottom = false;
            ground.StressAltitude = -3.5;
            ground.StressGLDepth = ground.StressAltitude - ground.GroundTopAltitude;
            input.GenerateSoilPiles();
            Assert.AreEqual(2 * cu, DeepHeadElement().PyFrontTop, 1e-9, "地中応力計算用Z から有効応力を数えるのに、粘性土の深さを地表から測っています");
        }

        /// <summary>
        /// 地盤ウィンドウ: 「地中応力計算用深さを各杭頭からとする」を選ぶと、使わなくなる地中応力計算用標高・深度は入力できない。
        /// チェックボックスは深度の行の下にあり、標高の欄と深度の欄は右端がそろう (2026-10-24 の利用者の指摘)。
        /// </summary>
        [TestMethod]
        public void GroundWindow_StressPlaneInputsAreLockedWhileCountingFromEachPileHead()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (model == null) { Assert.Inconclusive(error); return; }
            OnSta(() =>
            {
                var main = new MainWindowViewModel { CurrentInputModel = model };
                var vm = new GroundLayerViewModel(main);
                var w = new PileDesign.Views.GroundWindow
                {
                    DataContext = vm, ShowActivated = false, WindowState = System.Windows.WindowState.Normal, Width = 1600, Height = 1000,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual, Left = -20000, Top = -20000,
                };
                w.Show();
                try
                {
                    w.UpdateLayout();
                    var ground = vm.GroundInput!;
                    Assert.IsFalse(ground.IsStressFromPileFoundationBottom, "(前提) 既定は選ばない");
                    Assert.IsTrue(w.TextBoxGroundStressAltitude.IsEnabled && w.TextBoxStressGLDepth.IsEnabled, "選ばないときは入力できる");
                    StringAssert.Contains(w.CheckBoxStressFromPileHead.Content as string, "地中応力計算用深さを各杭頭からとする");

                    System.Windows.Point At(System.Windows.FrameworkElement e) => e.TranslatePoint(new System.Windows.Point(e.ActualWidth, 0), w);
                    Assert.IsTrue(At(w.CheckBoxStressFromPileHead).Y > At(w.TextBoxStressGLDepth).Y, "チェックボックスが深度の行より上にあります");
                    Assert.AreEqual(At(w.TextBoxStressGLDepth).X, At(w.TextBoxGroundStressAltitude).X, 0.5, "地中応力計算用の標高と深度の欄の右端がそろっていません");
                    Assert.AreEqual(At(w.TextBoxGroundWaterGLDepth).X, At(w.TextBoxGroundWaterTableAltitude).X, 0.5, "地下水位の標高と深度の欄の右端がそろっていません");
                    Assert.AreEqual(At(w.TextBoxGroundGLDepth).X, At(w.TextBoxGroundTopAltitude).X, 0.5, "孔口の標高と深度の欄の右端がそろっていません");

                    ground.IsStressFromPileFoundationBottom = true;
                    w.UpdateLayout();
                    Assert.IsFalse(w.TextBoxGroundStressAltitude.IsEnabled, "各杭頭から数えるのに、地中応力計算用標高を入力できます");
                    Assert.IsFalse(w.TextBoxStressGLDepth.IsEnabled, "各杭頭から数えるのに、地中応力計算用深度を入力できます");
                    Assert.IsTrue(w.TextBoxGroundWaterTableAltitude.IsEnabled, "地下水位まで入力できなくなっています");

                    ground.IsStressFromPileFoundationBottom = false;
                    w.UpdateLayout();
                    Assert.IsTrue(w.TextBoxGroundStressAltitude.IsEnabled && w.TextBoxStressGLDepth.IsEnabled, "選択を外しても入力できるように戻りません");
                }
                finally { w.Close(); }
            });
        }

        [TestMethod]
        public void GroundAltitudes_FollowTheirGLDepths()
        {
            var g = new GroundInput { GroundTopAltitude = 1.5 };
            g.GroundWaterGLDepth = -3.0;
            g.StressGLDepth = -1.0;
            g.GroundWaterTableAltitude = 0.0;   // 食い違い (GL−3 なら −1.5)
            g.StressAltitude = 0.5;              // 食い違いなし
            g.GroundLayers = [new GroundLayerInput { BottomGLDepth = -4.0, BottomAltitude = -4.0, LayerThickness = 4.0 }];
            g.GroundMassesData = [new GroundMassDataInput { GLDepth = -2.0, AltitudeDepth = -0.5 }];

            var fixes = g.SyncAltitudesFromGLDepths();
            Assert.AreEqual(-1.5, g.GroundWaterTableAltitude, 1e-12);
            Assert.AreEqual(0.5, g.StressAltitude, 1e-12);
            Assert.AreEqual(-2.5, g.GroundLayers[0].BottomAltitude, 1e-12);
            Assert.AreEqual(-0.5, g.GroundMassesData[0].AltitudeDepth, 1e-12);
            Assert.AreEqual(2, fixes.Count, "直した項目: " + string.Join(" / ", fixes));
            Assert.IsTrue(fixes.Any(f => f.StartsWith("地下水位")) && fixes.Any(f => f.StartsWith("土層の下端")));
            Assert.AreEqual(0, g.SyncAltitudesFromGLDepths().Count, "合っているのに直したと言っています");
        }

        /// <summary>
        /// ファイルを開いたときに地盤の標高を GL 深さへ合わせ、その地盤を使う土層-杭セットの地盤ばねを組み直す。
        /// 結果があれば再解析を促す。
        /// </summary>
        [TestMethod]
        public void LoadingAFile_AlignsTheGroundAndRebuildsTheSoilPileSets()
        {
            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            // 読み込んだ地盤は地盤ウィンドウ (の VM) が見ていて、標高を変えると GL 深さも合わせてしまう。
            // ファイルから読んだ直後と同じく、誰も見ていない地盤にする
            input.GroundsInput[0] = input.GroundsInput[0].DeepCopy();
            input.GroundsInput[0].SyncAltitudesFromGLDepths();   // 揃った地盤から始める (読み方に依らない)
            input.GenerateSoilPiles();
            var ground = input.GroundAt(1)!;
            var expected = input.ElementDivision.SoilPiles[0].HorizontalSoilReactions.Select(r => r.PyFrontTop).ToArray();

            ground.GroundWaterTableAltitude = ground.GroundTopAltitude;   // 地下水位を地表にずらす (GL 深さはそのまま)
            input.ElementDivision.SoilPiles[0].UpdateProperties();
            var shifted = input.ElementDivision.SoilPiles[0].HorizontalSoilReactions.Select(r => r.PyFrontTop).ToArray();
            CollectionAssert.AreNotEqual(expected, shifted, "テストの前提: 地下水位を変えても py が変わりません");

            var fixes = MainWindowViewModel.SyncGroundAltitudesOnLoad(input);
            Assert.AreEqual(1, fixes.Count);
            StringAssert.Contains(fixes[0], "地盤No.1");
            CollectionAssert.AreEqual(expected, input.ElementDivision.SoilPiles[0].HorizontalSoilReactions.Select(r => r.PyFrontTop).ToArray(),
                "土層-杭セットの地盤ばねを、直した地盤で組み直していません");

            string load = TestSource.MethodBody(TestSource.Read("Graphics_r1", "ViewModels", "MainWindowViewModel.FileIO.cs"),
                "private void ApplyPostLoadProtocol(");
            StringAssert.Contains(load, "SyncGroundAltitudesOnLoad(CurrentInputModel)");
            StringAssert.Contains(load, "if (CurrentModel != null) changedSinceAnalysisOnLoad = true;", "直したのに再解析を促していません");
        }

        /// <summary>例題は GL 深さだけを持つ。地盤ウィンドウを通さずに読んでも標高が揃う (実機・テストとも)。</summary>
        [TestMethod]
        public void ExampleGrounds_HaveAltitudesThatMatchTheirGLDepths()
        {
            var data = GroundExampleLoader.LoadFromFile("Example10");
            var g = new GroundInput();
            GroundExampleLoader.ApplyToGroundInput(g, data);
            Assert.AreEqual(g.GroundWaterGLDepth + g.GroundTopAltitude, g.GroundWaterTableAltitude, 1e-12, "地下水位の標高が GL 深さと違います");
            Assert.AreEqual(0, g.SyncAltitudesFromGLDepths().Count, "例題を読んだ直後に食い違いが残っています");

            var (input, err) = IntegrationTests.BuildExampleInputModel("Example10", "PileExample10");
            if (input == null) { Assert.Inconclusive("例題ファイルなし: " + err); return; }
            var tested = input.GroundAt(1)!;
            Assert.AreEqual(tested.GroundWaterGLDepth + tested.GroundTopAltitude, tested.GroundWaterTableAltitude, 1e-12,
                "テストが読む例題の地下水位の標高が GL 深さと違います (回帰の基準が実機と違う条件になる)");
        }
    }
}

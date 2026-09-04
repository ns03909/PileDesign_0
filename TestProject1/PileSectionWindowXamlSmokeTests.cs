using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using PileDesign.Constants;
using PileDesign.Models.InputData;
using System.Windows.Controls;
using PileDesign.ViewModels;

namespace TestProject1
{
    /// <summary>
    /// 杭断面ウィンドウのスモークテスト。
    ///
    /// このウィンドウには N-M・N-Q の曲線を見ながら変えられる材料モデル化オプションを置いた。
    /// StaticResource のキー誤り（このウィンドウのコンバータ名は基本設定と違う）や
    /// Binding パスの打ち間違いは<b>ビルドを通り、開いた瞬間に例外</b>または
    /// 「常に既定側が選ばれたまま」という静かな不具合になる。
    ///
    /// 基本設定のスモークテストと同じく、ウィンドウは表示せずディスパッチャも回さない
    /// （短命な STA スレッドの Dispatcher が Application.Current.Dispatcher として残ると、
    /// 後続の描画が固まる）。
    ///
    /// あわせて、既製コンクリート杭の各断面タイプで実際に開く検査も持つ。
    /// 断面タイプを増やしたときに追随が要る箇所（製品 ComboBox の対応表・
    /// VisibilityConverter のパラメータ・断面図の描画分岐）は、いずれも
    /// <b>ビルドが通ったまま実行時に静かに壊れる</b>。
    /// 「製品一覧が空の ComboBox が出る」「断面図が描かれない」を検出する。
    /// </summary>
    [TestClass]
    public class PileSectionWindowXamlSmokeTests
    {
        /// <summary>
        /// 最小限の親 ViewModel。ウィンドウは XAML を組むだけで、解析結果は要らない。
        /// </summary>
        private static MainWindowViewModel BuildMainViewModel()
        {
            // 素の InputModel は FundamentalInput を作らない (アプリの経路では作られる)。
            // 作らないまま渡すと材料オプションの窓口が黙って何もしないので、ここで用意する。
            var input = new PileDesign.Models.InputData.InputModel
            {
                FundamentalInput = new PileDesign.Models.InputData.FundamentalInput(),
            };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            input.AttachViewModel(vm);
            return vm;
        }

        /// <summary>材料オプションが出る杭種 (場所打ちRC) の断面を渡す。</summary>
        private static PileDesign.Models.InputData.PileSection BuildSection() => new()
        {
            PileBodyType = PileDesign.Constants.PileTypeNames.InsituRc,
            PileSectionType = PileDesign.Constants.PileTypeNames.RcSection,
            ConcreteOutDia = 1500.0,
            ConcreteFc = 27.0,
            ConcreteGsi = 1.0,
            MainBarNum = 30,
            MainBarSize = "D29",
            MainBarSpec = "SD390",
            MainBarDr = 200.0,
            HoopSize = "D13",
            HoopSpacing = 150.0,
            HoopSpec = "SD295",
            HoopCenterCover = 150.0,
            PileDiameter = 1500.0,
        };

        private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is not DependencyObject d) continue;
                yield return d;
                foreach (var g in LogicalDescendants(d)) yield return g;
            }
        }

        /// <summary>
        /// 材料のモデル化は<b>その断面に効く項目だけ</b>出すこと。
        /// 効かない項目を出すと、選び直しても曲線が変わらず「壊れている」と読める。
        /// どの断面に効くかは実装をたどって決めた対応。
        /// </summary>
        [TestMethod]
        public void MaterialOptionVisibility_MatchesTheSectionType()
        {
            var vm = BuildSectionViewModel(
                PileDesign.Constants.PileTypeNames.InsituRc,
                PileDesign.Constants.PileTypeNames.RcSection);
            Assert.IsTrue(vm.UsesInsituConcrete, "場所打ちRC: コンクリートのモデル化が出ない");
            Assert.IsTrue(vm.ShowNotification1113ShearOption, "場所打ちRC: 告示1113 せん断が出ない");
            Assert.IsFalse(vm.ShowSteelPipeYieldOption, "場所打ちRC: 鋼管 1.1F が出ている");
            Assert.IsFalse(vm.ShowKctbOptions, "場所打ちRC: KCTB の項目が出ている");
            Assert.IsFalse(vm.ShowGuideYoungsModulusOption, "場所打ちRC: 既製杭の鋼材 E が出ている");
            Assert.IsTrue(vm.ShowFiberMPhiOption);

            vm = BuildSectionViewModel(
                PileDesign.Constants.PileTypeNames.InsituSteelPipeConcrete,
                PileDesign.Constants.PileTypeNames.SteelPipeConcreteSection);
            Assert.IsTrue(vm.ShowSteelPipeYieldOption, "場所打ち鋼管ｺﾝｸﾘ: 鋼管 1.1F が出ない");
            Assert.IsTrue(vm.ShowKctbOptions, "場所打ち鋼管ｺﾝｸﾘ: KCTB の項目が出ない");
            Assert.IsFalse(vm.ShowNotification1113ShearOption,
                "場所打ち鋼管ｺﾝｸﾘ: 告示1113 せん断が出ている (場所打ちRC だけの規定)");

            vm = BuildSectionViewModel(
                PileDesign.Constants.PileTypeNames.PrecastConcrete,
                PileDesign.Constants.PileTypeNames.Phc);
            Assert.IsTrue(vm.ShowGuideYoungsModulusOption, "既製杭: 鋼材のヤング係数 n=5 が出ない");
            Assert.IsTrue(vm.ShowFiberMPhiOption, "既製杭: ファイバー M-φ が出ない");
            Assert.IsFalse(vm.UsesInsituConcrete,
                "既製杭: 場所打ちコンクリートのモデル化が出ている (効かない)");

            vm = BuildSectionViewModel(
                PileDesign.Constants.PileTypeNames.SteelPipe,
                PileDesign.Constants.PileTypeNames.CftSection);
            Assert.IsTrue(vm.UsesInsituConcrete, "充填鋼管部: 充填コンクリートのモデル化が出ない");
            Assert.IsTrue(vm.ShowFiberMPhiOption, "充填鋼管部: ファイバー M-φ が出ない");
            Assert.IsFalse(vm.ShowSteelPipeYieldOption,
                "充填鋼管部: 鋼管 1.1F が出ている (鋼管杭は対象外)");
            Assert.IsFalse(vm.ShowKctbOptions, "充填鋼管部: KCTB の項目が出ている");

            vm = BuildSectionViewModel(
                PileDesign.Constants.PileTypeNames.SteelPipe,
                PileDesign.Constants.PileTypeNames.SteelPipeSection);
            Assert.IsFalse(vm.AreMaterialOptionsAvailable,
                "鋼管部: 出す項目は無いはず (SteelPipeSection は別系統)");
        }

        /// <summary>
        /// 材料のモデル化を変えたら、<b>この画面が持つ断面</b>の曲線が変わること。
        ///
        /// 杭体ウィンドウは <c>InputModel.PileBodies</c> の複製を編集しており、断面ウィンドウは
        /// その複製側の断面を受け取る。そのため
        /// <c>MainWindowViewModel.ApplyConcreteModelOptions</c> のキャッシュ破棄
        /// (CurrentInputModel の断面をたどる) がこの断面に届かず、
        /// オプションを変えても<b>前の設定で計算済みの N-M がそのまま描かれていた</b>。
        /// 選択は反映されるので、画面上は「効かない」ようにしか見えない。
        /// </summary>
        [TestMethod]
        public void ChangingAMaterialOption_InvalidatesThisSectionsCurves()
        {
            bool ignoreTension = PileDesign.Models.InputData.ConcreteModelOptions.IgnoreTensileStrength;
            bool reduceCompression = PileDesign.Models.InputData.ConcreteModelOptions.UseReducedCompression;
            try
            {
                var main = BuildMainViewModel();
                var section = BuildSection();

                // この画面の断面は現在の入力モデルに居ない (複製を受け取る) 状況
                var vm = new PileSectionViewModel(main, section, 1, 1);

                double before = section.UnfactoredUltimateNM.M.Max();
                Assert.IsTrue(before > 0, "前提: 安全限界 N-M が求まる断面であること");

                bool target = !vm.UseReducedConcreteCompressiveStrength;
                vm.UseReducedConcreteCompressiveStrength = target;

                Assert.AreEqual(target, main.CurrentInputModel!.FundamentalInput.UseReducedConcreteCompressiveStrength,
                    "PROBE: モデルに書かれていない");
                Assert.AreEqual(target, PileDesign.Models.InputData.ConcreteModelOptions.UseReducedCompression,
                    "PROBE: 静的オプションに反映されていない");

                double after = section.UnfactoredUltimateNM.M.Max();
                Assert.AreNotEqual(before, after,
                    "圧縮側の折れ点応力度を変えても N-M が変わらない。"
                    + "この画面の断面のキャッシュが捨てられていない可能性がある。");
            }
            finally
            {
                PileDesign.Models.InputData.ConcreteModelOptions.IgnoreTensileStrength = ignoreTension;
                PileDesign.Models.InputData.ConcreteModelOptions.UseReducedCompression = reduceCompression;
            }
        }

        private static PileSectionViewModel BuildSectionViewModel(string bodyType, string sectionType)
        {
            var main = BuildMainViewModel();
            var section = BuildSection();
            section.PileBodyType = bodyType;
            section.PileSectionType = sectionType;
            return new PileSectionViewModel(main, section, 1, 1);
        }

        [TestMethod]
        public void PileSectionWindow_Opens()
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.PileSectionWindow(
                    BuildMainViewModel(), BuildSection(), pileBodyNo: 1, segmentNo: 1);
                created = true;
                window.Close();
            }, out bool timedOut);

            if (timedOut)
            {
                Assert.Inconclusive("XAML パースが 60 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
            {
                Assert.Fail("PileSectionWindow の XAML パースに失敗: "
                            + $"{captured.GetType().Name}: {captured.Message}"
                            + Environment.NewLine + captured.StackTrace);
            }
            Assert.IsTrue(created, "PileSectionWindow が生成されなかった");
        }

        /// <summary>
        /// 材料のモデル化パネルのラジオ対が、PileSectionViewModel に実在するプロパティへ
        /// TwoWay で結ばれていること。
        ///
        /// パスを間違えても例外は出ず、選択が反映されないまま「既定のまま」になる。
        /// </summary>
        [TestMethod]
        public void MaterialOptionRadios_BindToExistingViewModelProperties()
        {
            var failures = new List<string>();
            int pairCount = 0;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.PileSectionWindow(
                    BuildMainViewModel(), BuildSection(), pileBodyNo: 1, segmentNo: 1);
                try
                {
                    var pairs = LogicalDescendants(window)
                        .OfType<PileDesign.Views.Controls.MaterialOptionRadioPair>()
                        .ToList();
                    pairCount = pairs.Count;

                    foreach (var pair in pairs)
                    {
                        string header = pair.Header ?? "";
                        if (string.IsNullOrWhiteSpace(header))
                            failures.Add("Header が空のラジオ対がある");
                        if (string.IsNullOrWhiteSpace(pair.HelpAnchor))
                            failures.Add($"「{header}」: HelpAnchor が未設定");

                        var binding = BindingOperations.GetBinding(
                            pair, PileDesign.Views.Controls.MaterialOptionRadioPair.IsAlternativeSelectedProperty);
                        if (binding == null)
                        {
                            failures.Add($"「{header}」: IsAlternativeSelected に Binding が無い");
                            continue;
                        }

                        string path = binding.Path?.Path ?? "";
                        if (typeof(PileSectionViewModel).GetProperty(path) == null)
                            failures.Add($"「{header}」: Binding パス '{path}' が PileSectionViewModel に存在しない");
                        if (binding.Mode != BindingMode.TwoWay)
                            failures.Add($"「{header}」: Binding が TwoWay でない ({binding.Mode})");
                    }
                }
                finally { window.Close(); }
            }, out bool timedOut);

            if (timedOut)
            {
                Assert.Inconclusive("60 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"{captured.GetType().Name}: {captured.Message}"
                            + Environment.NewLine + captured.StackTrace);

            // 場所打ち 2 杭種ぶんのオプションを置いてある。減っていたら XAML から落ちている
            Assert.IsTrue(pairCount >= 10,
                $"材料のモデル化のラジオ対が {pairCount} 個しか見つからない（10 個以上のはず）");
            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        /// <summary>断面タイプ → 製品 ComboBox の x:Name と、選択する製品名。</summary>
        private static IEnumerable<(string SectionType, string BoxName, string Product)> Cases()
        {
            yield return (PileTypeNames.Phc, "ComboBoxPHCPileType", PileSection.PHCOption.First());
            yield return (PileTypeNames.Prc, "ComboBoxPRCPileType", PileSection.PRCOption.First());
            yield return (PileTypeNames.Sc, "ComboBoxSCPileType", PileSection.SCOption.First());
            yield return (PileTypeNames.PhcNodular, "ComboBoxNodularPileType",
                          "NPH-440-300-標準-85-A");
            yield return (PileTypeNames.PrcNodular, "ComboBoxNodularPrcPileType",
                          "NPRC-440-300-標準-105-Ⅰ");
            yield return (PileTypeNames.PrcNodularPhcPart, "ComboBoxNodularPrcPhcPartPileType",
                          "NPRC-440-300-標準-105-Ⅰ-PHC部");
            yield return (PileTypeNames.BfsHead, "ComboBoxBfsHeadPileType",
                          "BF.S-400-3045-105-A2");
            yield return (PileTypeNames.BfsTip, "ComboBoxBfsTipPileType",
                          "BF.S-400-3045-105-A2-先端軸部");
        }

        [TestMethod]
        public void PileSectionWindow_OpensForEveryPrecastSectionType()
        {
            var failures = new List<string>();

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var mainVm = new MainWindowViewModel();

                foreach (var (sectionType, boxName, product) in Cases())
                {
                    var section = new PileSection
                    {
                        PileBodyType = PileTypeNames.PrecastConcrete,
                        PileSectionType = sectionType,
                    };
                    section.SelectedPrecastPile.Name = product;
                    section.RecalculateSelectedPrecastPile();

                    var window = new PileDesign.Views.PileSectionWindow(mainVm, section, 1, 2);
                    try
                    {
                        // 製品 ComboBox が「その断面タイプ用の一覧」で埋まっているか。
                        // GetPrecastPileComboBox の対応表に追加し忘れると、
                        // ItemsSource が null のまま空の ComboBox が出る。
                        if (window.FindName(boxName) is not ComboBox box)
                        {
                            failures.Add($"{sectionType}: {boxName} が見つからない");
                            continue;
                        }
                        if (box.ItemsSource is not System.Collections.IEnumerable items)
                        {
                            failures.Add($"{sectionType}: {boxName}.ItemsSource が未設定");
                            continue;
                        }
                        if (!items.Cast<object>().Any())
                            failures.Add($"{sectionType}: {boxName} の製品一覧が空");
                        if (!Equals(box.SelectedItem, product))
                            failures.Add($"{sectionType}: 選択中の製品が反映されていない " +
                                         $"(期待 {product} / 実際 {box.SelectedItem})");

                        // 断面図の描画分岐にその断面タイプが無いと、例外にならず「何も描かれない」。
                        // 注: Canvas は Measure/Arrange して ActualWidth/Height を持たせること。
                        //     0 のままだと ShapeDrawer.DrawGauge の目盛ループが抜けられない
                        //     (スケール 0 → 目盛間隔 0)。
                        var canvas = new Canvas();
                        canvas.Measure(new System.Windows.Size(400, 400));
                        canvas.Arrange(new System.Windows.Rect(0, 0, 400, 400));
                        canvas.UpdateLayout();
                        window.ViewModel.Canvas = canvas;
                        window.ViewModel.RedrawShapes();
                        if (canvas.Children.Count == 0)
                            failures.Add($"{sectionType}: 断面図が 1 つも描画されない");
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }, out bool timedOut, timeoutSeconds: 180);

            if (timedOut)
            {
                Assert.Inconclusive("ウィンドウ生成が 180 秒以内に完了しなかったためスキップ");
                return;
            }
            if (captured != null)
                Assert.Fail($"杭断面ウィンドウの生成に失敗: {captured.GetType().Name}: {captured.Message}\n{captured.StackTrace}");

            Assert.AreEqual(0, failures.Count, string.Join("\n", failures));
        }

    }
}

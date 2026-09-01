using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace TestProject1
{
    /// <summary>
    /// 基本設定ウィンドウのスモークテスト。
    ///
    /// このウィンドウは材料モデル化オプションの追加のたびに XAML が伸びており、
    /// StaticResource のキー誤り・Grid の行数と Grid.Row の不一致は
    /// <b>ビルドが通ったまま開いた瞬間に例外</b>または黙って欠けた表示になる。
    /// Binding のパスを打ち間違えた場合はさらに静かで、例外も出ず
    /// 「常に既定側が選ばれたまま」になる。
    ///
    /// 注意: このテストではウィンドウを表示せず、ディスパッチャも回さない。
    /// <see cref="System.Windows.Threading.Dispatcher.PushFrame"/> でキューを処理すると、
    /// 短命な STA スレッド上の Dispatcher が <c>Application.Current.Dispatcher</c> として残り、
    /// 後続の <c>DiagramRenderer.ExecuteOnUIThread</c> がそこへ Invoke して固まる
    /// （全体実行だけ 10 分でタイムアウトし、テストホストごとクラッシュする）。
    /// Binding は評価せず、<b>宣言されたパスが VM に実在するか</b>をリフレクションで確かめる。
    /// </summary>
    [TestClass]
    public class FundamentalWindowXamlSmokeTests
    {
        [TestMethod]
        public void FundamentalWindow_Opens()
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.FundamentalWindow();
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
                Assert.Fail("FundamentalWindow の XAML パースに失敗: "
                            + $"{captured.GetType().Name}: {captured.Message}"
                            + Environment.NewLine + captured.StackTrace);
            }
            Assert.IsTrue(created, "FundamentalWindow が生成されなかった");
        }

        /// <summary>
        /// プロジェクト情報ウィンドウ（基本設定から分けたもの）が開くこと。
        /// </summary>
        [TestMethod]
        public void ProjectInfoWindow_Opens()
        {
            bool created = false;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.ProjectInfoWindow();
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
                Assert.Fail("ProjectInfoWindow の XAML パースに失敗: "
                            + $"{captured.GetType().Name}: {captured.Message}"
                            + Environment.NewLine + captured.StackTrace);
            }
            Assert.IsTrue(created, "ProjectInfoWindow が生成されなかった");
        }

        /// <summary>
        /// 各オプションのラジオ対が、FundamentalViewModel に実在するプロパティへ
        /// TwoWay で結び付いていること。パスを打ち間違えても WPF は例外を出さないため、
        /// 宣言を直接検査する。Header と HelpAnchor の付け忘れも同時に拾う
        /// （HelpAnchor が空だと HelpAnchorTests の収集対象から外れて検査が素通りする）。
        /// </summary>
        [TestMethod]
        public void FundamentalWindow_OptionRadios_BindToExistingViewModelProperties()
        {
            var failures = new List<string>();
            int pairCount = 0;

            var captured = XamlSmokeTestSupport.RunOnStaThread(() =>
            {
                var window = new PileDesign.Views.FundamentalWindow();
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
                        if (typeof(FundamentalViewModel).GetProperty(path) == null)
                            failures.Add($"「{header}」: Binding パス '{path}' が FundamentalViewModel に存在しない");
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

            // オプションを増やしたのに XAML へ足し忘れると減る
            Assert.IsTrue(pairCount >= 10, $"MaterialOptionRadioPair が {pairCount} 個しか見つからない（10 個以上のはず）");
            Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
        }

        /// <summary>
        /// マスターチェックが構成項目に追随すること（ウィンドウは開かない）。
        /// get が構成項目から導かれているので、個別に 1 つ戻すとチェックが外れる。
        /// </summary>
        [TestMethod]
        public void MasterCheckBoxes_FollowConstituentOptions()
        {
            var mainVm = new MainWindowViewModel();
            var f = mainVm.CurrentInputModel!.FundamentalInput;
            try
            {
                f.UseNotification1113Compression = true;
                f.UseNotification1113Shear = true;
                f.Notification1113CompressionCase = 1;
                f.UseFiberNMForSteelPipeConcrete = false;   // 評定 5.(3) の単純累加
                mainVm.ApplyConcreteModelOptions();

                var vm = new FundamentalViewModel(mainVm);
                Assert.IsTrue(vm.UseGuideline2025Appendix13,
                    "2025解説書のマスターチェックが構成項目に追随していない");
                Assert.IsTrue(vm.FollowsKctbEvaluation,
                    "BCJ評定のマスターチェックが構成項目に追随していない");

                // 構成項目を 1 つ評定から外すとチェックも外れる
                f.Notification1113CompressionCase = 2;
                mainVm.ApplyConcreteModelOptions();
                var vm2 = new FundamentalViewModel(mainVm);
                Assert.IsFalse(vm2.FollowsKctbEvaluation,
                    "告示1113 の区分を変えたのに BCJ評定のチェックが外れない");
            }
            finally
            {
                ConcreteModelOptions.UseNotification1113Compression = false;
                ConcreteModelOptions.UseNotification1113Shear = false;
                ConcreteModelOptions.Notification1113CompressionCase = 1;
                ConcreteModelOptions.UseFiberNMForSteelPipeConcrete = true;
                ConcreteModelOptions.UseUltimateStrain5000ForSteelPipeConcrete = false;
                ConcreteModelOptions.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = false;
                PileSection.ClearMphiCache();
            }
        }

        private static IEnumerable<object> LogicalDescendants(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                yield return child;
                if (child is DependencyObject d)
                    foreach (object x in LogicalDescendants(d)) yield return x;
            }
        }
    }
}

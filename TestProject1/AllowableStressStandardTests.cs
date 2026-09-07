using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 使用限界・損傷限界の許容応力度の規準（基礎部材の強度と変形性能 / 告示1113(第8)）は、
    /// 許容圧縮応力度と許容せん断で<b>常に同じ</b>であること。
    ///
    /// 以前は 2 つを別々に選べた（画面に 2 ペア＋一括切替のチェック）が、別々の規準にすることは
    /// 実務上あり得ないので 1 つの選択にまとめた。永続化は互換のため従来の 2 フラグのままなので、
    /// 「片方だけ書き換える経路が残っていないか」「読み込んだ旧ファイルが揃えられるか」を固定する。
    /// </summary>
    [TestClass]
    public class AllowableStressStandardTests
    {
        [TestCleanup]
        public void Cleanup()
        {
            ConcreteModelOptions.UseNotification1113 = false;
            ConcreteModelOptions.Notification1113CompressionCase = 1;
            PileSection.ClearMphiCache();
        }

        [TestMethod]
        public void InputModelSetterWritesBothPersistedFlags()
        {
            var f = new FundamentalInput();
            f.UseNotification1113 = true;
            Assert.IsTrue(f.UseNotification1113Compression && f.UseNotification1113Shear, "告示側: 圧縮とせん断の両方が立つ");
            f.UseNotification1113 = false;
            Assert.IsFalse(f.UseNotification1113Compression || f.UseNotification1113Shear, "基礎部材側: 両方が下りる");
        }

        [TestMethod]
        public void MixedFlagsFromAnOldFileAreNormalizedToTheCompressionSide()
        {
            // 圧縮だけ告示だった旧ファイル → 告示に揃う
            var f = new FundamentalInput { UseNotification1113Compression = true, UseNotification1113Shear = false };
            Assert.IsTrue(f.NormalizeNotification1113(), "食い違いを検出して true を返す");
            Assert.IsTrue(f.UseNotification1113Shear, "せん断が圧縮側 (告示) に揃う");

            // せん断だけ告示だった旧ファイル → 基礎部材に揃う
            f = new FundamentalInput { UseNotification1113Compression = false, UseNotification1113Shear = true };
            Assert.IsTrue(f.NormalizeNotification1113());
            Assert.IsFalse(f.UseNotification1113Shear, "せん断が圧縮側 (基礎部材) に揃う");

            // 揃っていれば何もしない
            f = new FundamentalInput { UseNotification1113Compression = true, UseNotification1113Shear = true };
            Assert.IsFalse(f.NormalizeNotification1113());
        }

        [TestMethod]
        public void StaticOptionSetterWritesBothFlags()
        {
            ConcreteModelOptions.UseNotification1113 = true;
            Assert.IsTrue(ConcreteModelOptions.UseNotification1113Compression && ConcreteModelOptions.UseNotification1113Shear);
            Assert.IsTrue(ConcreteModelOptions.UseAllowableStressLabels, "呼称も長期許容/短期許容に切り替わる");
            ConcreteModelOptions.UseNotification1113 = false;
            Assert.IsFalse(ConcreteModelOptions.UseNotification1113Compression || ConcreteModelOptions.UseNotification1113Shear);
        }

        [TestMethod]
        public void FundamentalViewModelExposesOnlyTheUnifiedOption()
        {
            var mainVm = new MainWindowViewModel();
            var f = mainVm.CurrentInputModel!.FundamentalInput;
            var vm = new FundamentalViewModel(mainVm);
            try
            {
                vm.UseNotification1113 = true;
                Assert.IsTrue(f.UseNotification1113Compression && f.UseNotification1113Shear,
                    "基本設定で告示側を選ぶと、入力モデルの圧縮・せん断の両方が告示になる");
                Assert.IsTrue(ConcreteModelOptions.UseNotification1113Compression && ConcreteModelOptions.UseNotification1113Shear,
                    "静的オプションにも両方が反映される");
                Assert.IsTrue(vm.Notification1113CaseEnabled, "区分の選択が有効になる");

                vm.UseNotification1113 = false;
                Assert.IsFalse(f.UseNotification1113Compression || f.UseNotification1113Shear);
            }
            finally
            {
                vm.UseNotification1113 = false;
            }
        }

        /// <summary>
        /// 画面は統一した 1 つのプロパティだけをバインドし、圧縮・せん断の個別フラグを直接触らないこと。
        /// 個別のバインドが残ると、片方だけ変わる経路が復活する。
        /// </summary>
        [TestMethod]
        public void ViewsDoNotBindTheIndividualFlags()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(AllowableStressStandardTests).Assembly.Location)!);
            for (; dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Graphics_r1", "Help", "help.html"))) break;
            Assert.IsNotNull(dir, "ソリューションルートが見つかりません");

            var forbidden = new Regex(@"Binding\s+(UseNotification1113Compression|UseNotification1113Shear|UseGuideline2025Appendix13)\b");
            foreach (string file in Directory.GetFiles(Path.Combine(dir!.FullName, "Graphics_r1", "Views"), "*.xaml", SearchOption.AllDirectories))
            {
                string xaml = File.ReadAllText(file);
                Assert.IsFalse(forbidden.IsMatch(xaml), $"{Path.GetFileName(file)}: 圧縮・せん断の個別フラグが画面にバインドされています");
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Common;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 一般節点の座標に有限でない値 (NaN・±∞) が入らないこと。入ろうとしたら理由を示して失敗すること。
    ///
    /// 入口は 3 つあった。表への貼り付け (<see cref="PasteNonFiniteTests"/>)、プロパティパネル、
    /// ファイルの読み込み (保存形式が「"NaN"」「"Infinity"」を読めるため)。
    /// 座標そのものが拒否し、プロパティパネルは入力の時点で知らせ、
    /// 読み込みは失敗させて理由を示す (<c>HandleFileLoadError</c>)。
    /// </summary>
    [TestClass]
    public class NonFiniteNodeCoordinateTests
    {
        [DataTestMethod]
        [DataRow(double.NaN, "NaN")]
        [DataRow(double.PositiveInfinity, "+∞")]
        [DataRow(double.NegativeInfinity, "-∞")]
        public void CoordinateRejectsNonFinite(double bad, string symbol)
        {
            var node = new InputNode { No = 3, X = 1.5, Y = 2.5, Z = -1.0 };

            var ex = Assert.ThrowsException<NonFiniteValueException>(() => node.X = bad);
            StringAssert.Contains(ex.Message, "節点 3 の X 座標", "どの節点のどの座標かが示されていません");
            StringAssert.Contains(ex.Message, symbol, "値の種別が示されていません");
            Assert.AreEqual(1.5, node.X, "拒否したのに値が変わっています");

            Assert.ThrowsException<NonFiniteValueException>(() => node.Y = bad);
            Assert.ThrowsException<NonFiniteValueException>(() => node.Z = bad);
            Assert.AreEqual(2.5, node.Y);
            Assert.AreEqual(-1.0, node.Z);
        }

        [TestMethod]
        public void FiniteValuesStillSet()
        {
            var node = new InputNode { X = -12.345, Y = 0.0, Z = 1e6 };
            Assert.AreEqual(-12.345, node.X);
            Assert.AreEqual(1e6, node.Z);
        }

        /// <summary>
        /// 座標に「"NaN"」と書かれたファイルは、読み込みが失敗し、理由が内側の例外から取り出せること。
        /// 読み込みが例外を握りつぶして、NaN 抜きで (あるいは 0 で) 読めてしまわないこと。
        /// </summary>
        [TestMethod]
        public void LoadingAFileWithNaNCoordinateFails()
        {
            // 実物の保存・読み込みと同じく、名前付きの浮動小数点 ("NaN") を読める設定にする
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };
            var svc = new FileOperationService(options);
            string file = Path.Combine(Path.GetTempPath(), $"NonFiniteNode_{Guid.NewGuid():N}.pdj");
            try
            {
                var input = new InputModel
                {
                    InputNodes = new ObservableCollection<InputNode> { new() { Type = NodeType.General, X = 1.25, Y = 2.5, Z = 0 } },
                };
                svc.SaveProjectData(file, input, new AnaModel());

                string json = File.ReadAllText(file);
                string broken = Regex.Replace(json, @"""X"":\s*1\.25", @"""X"": ""NaN""");
                Assert.AreNotEqual(json, broken, "保存ファイルに節点の X が見つかりません (形式が変わった?)");
                File.WriteAllText(file, broken);

                Exception? thrown = null;
                try
                {
                    var loaded = svc.LoadProjectData(file);
                    svc.ConvertToObservableCollections(loaded.InputModel!);
                }
                catch (Exception ex)
                {
                    thrown = ex;
                }

                Assert.IsNotNull(thrown, "座標が NaN のファイルがそのまま読めてしまいました");
                var reason = MainWindowViewModel.FindInnerException<NonFiniteValueException>(thrown);
                Assert.IsNotNull(reason,
                    $"読み込みの失敗から「有限でない値」の理由を取り出せません (利用者に原因が示されません): {thrown}");
                StringAssert.Contains(reason.Message, "X 座標");
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// プロパティパネルに「NaN」と入れても、座標は変わらず、表示は元の値に戻り、編集の記録も残らないこと。
        /// 以前は差の比較 (NaN は常に偽) をすり抜けて、そのまま座標に入っていた。
        /// </summary>
        [DataTestMethod]
        [DataRow("NaN")]
        [DataRow("Infinity")]
        [DataRow("-Infinity")]
        public void PropertyPanelRejectsNonFinite(string text)
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;   // 知らせるダイアログで止まらないように
            try
            {
                var vm = new MainWindowViewModel();
                var node = new InputNode { Type = NodeType.General, X = 1.5, Y = 2.0, Z = 0.0, IsSelected = true };
                vm.CurrentInputModel!.InputNodes!.Add(node);
                vm.UpdatePropertyPanel();
                vm.MarkProjectReplaced();

                var x = vm.SelectedItemProperties.FirstOrDefault(p => p.Name == "X");
                Assert.IsNotNull(x, "プロパティパネルに一般節点の X が出ていません");

                x.Value = text;   // 確定 (CommitAction が走る)

                Assert.AreEqual(1.5, node.X, $"「{text}」が座標に入りました");
                Assert.AreEqual("1.500", x.Value, "表示が元の値に戻っていません");
                Assert.IsFalse(vm.HasUnsavedWork, "拒否したのに編集の記録 (Undo) が積まれています");
            }
            finally
            {
                MessageService.IsUnattended = unattended;
            }
        }
    }
}

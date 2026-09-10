using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace TestProject1
{
    /// <summary>
    /// 入力の中から画面 (ViewModel) を辿るオブジェクトが、<b>自分の親</b>を見ること。
    ///
    /// <para><c>InputModel</c> の子は、画面から「いまの入力」を辿れるように
    /// <c>MainWindowViewModel</c> への参照を持っている。ここで
    /// <c>_mainWindowViewModel.CurrentInputModel</c> を直に読むと、
    /// <b>控えの中のオブジェクトが自分の親ではなく生の入力を読む</b>。</para>
    ///
    /// <para>控えは日常的に作られる。解析結果セットの <c>InputSnapshot</c>、
    /// メイン画面の Undo 履歴、荷重条件ウィンドウの控え。そのどれかを表示している間に
    /// 生の入力が違っていれば、値が混ざる。<c>LoadCase.SumV</c> は杭配置の軸力を
    /// 合算するので、控えの荷重ケースが生の杭配置を読むと、
    /// <b>同じ行の他の列と揃わない ΣV</b> が出る。</para>
    ///
    /// <para>杭配置 (<c>PileLayoutDataItem</c>) は以前この形を直して親を固定してあったが、
    /// <c>LoadCase</c> は固定されていなかった。実測すると控えの荷重ケースは
    /// 生の入力を指していた。</para>
    /// </summary>
    [TestClass]
    public class BackReferenceOwnerTests
    {
        [TestMethod]
        public void ASnapshotsChildren_LookAtTheSnapshot_NotTheLiveInput()
        {
            var (model, error) = IntegrationTests.BuildExampleInputModel("Example9", "PileExample9");
            if (model == null) { Assert.Inconclusive(error); return; }

            var vm = new MainWindowViewModel { CurrentInputModel = model };
            model.SetMainWindowViewModel(vm);

            var snapshot = model.DeepCopy();
            snapshot.SetMainWindowViewModel(vm);

            // 画面が持っているのは生の方
            Assert.AreSame(model, vm.CurrentInputModel, "前提が変わりました");

            int checkedCount = 0;

            var liveCases = model.LoadCasesInput.LoadCasesLevel1;
            var snapCases = snapshot.LoadCasesInput.LoadCasesLevel1;
            Assert.IsTrue(liveCases.Count > 0 && snapCases.Count > 0, "荷重ケースがありません");

            foreach (var lc in liveCases)
            {
                checkedCount++;
                Assert.AreSame(model, lc.InputModel,
                    "生の荷重ケースが生の入力を指していません");
            }

            foreach (var lc in snapCases)
            {
                checkedCount++;
                Assert.AreSame(snapshot, lc.InputModel,
                    "控えの荷重ケースが、控えではなく生の入力を指しています。"
                    + "ΣV が同じ行の他の列と揃わなくなります");
            }

            foreach (var p in snapshot.PileLayoutItems ?? [])
            {
                checkedCount++;
                Assert.AreSame(snapshot, p.InputModel,
                    "控えの杭配置が、控えではなく生の入力を指しています");
            }

            TestSource.AssertScanned(checkedCount, 4, "親を確かめた子");
        }

        /// <summary>
        /// 配線の入口が 2 つある (新規作成と 読込/Undo) ので、<b>どちらも同じ道具を通る</b>こと。
        /// 片方だけに書くと、その経路で開いたときだけ親が固定されない。
        /// </summary>
        [TestMethod]
        public void BothWiringEntryPoints_GoThroughTheSameHelper()
        {
            string src = TestSource.Read("Graphics_r1", "Models", "InputData", "InputModel.cs");

            foreach (string which in new[]
            {
                "public void SetMainWindowViewModel(MainWindowViewModel mainWindowViewModel)",
                "public void AttachViewModel(MainWindowViewModel mainWindowViewModel)",
            })
            {
                string body = Regex.Replace(TestSource.MethodBody(src, which), "//.*", "");

                // 新規作成の側は Reset() 経由で配る
                bool wires = body.Contains("WireLoadCases", StringComparison.Ordinal)
                    || body.Contains("Reset()", StringComparison.Ordinal);

                Assert.IsTrue(wires,
                    $"{which} が荷重ケースの配線を通っていません。"
                    + "この経路で開いたときだけ親が固定されません");
            }

            string reset = Regex.Replace(TestSource.MethodBody(src, "public void Reset()"), "//.*", "");
            StringAssert.Contains(reset, "WireLoadCases",
                "Reset が荷重ケースの配線を通っていません (新規作成の経路)");

            // 絞った一覧で配線すると、適用外の荷重ケースが親を持たないまま残る
            string helper = Regex.Replace(
                TestSource.MethodBody(src, "private void WireLoadCases(MainWindowViewModel mainWindowViewModel)"),
                "//.*", "");
            StringAssert.Contains(helper, "EveryLoadCase",
                "配線が絞った一覧 (AllLoadCases) を使っています。"
                + "いま適用外の荷重ケースだけ親を持たないまま残ります");
        }

        /// <summary>
        /// 親を固定できる子が、固定を<b>実際に持っている</b>こと。
        /// 「VM を持ち、InputModel を公開する」型は、この形を守る必要がある。
        /// </summary>
        [TestMethod]
        public void EveryTypeThatWalksToTheViewModel_PinsItsOwner()
        {
            var offenders = new System.Collections.Generic.List<string>();
            int scanned = 0;

            foreach (Type type in typeof(InputModel).Assembly.GetTypes())
            {
                if (type.Namespace != "PileDesign.Models.InputData") continue;
                if (type == typeof(InputModel)) continue;

                var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                bool holdsVm = fields.Any(f => f.FieldType == typeof(MainWindowViewModel));
                if (!holdsVm) continue;

                var exposes = type.GetProperty("InputModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (exposes == null) continue;

                scanned++;
                bool pins = fields.Any(f => f.FieldType == typeof(InputModel));
                if (!pins)
                    offenders.Add(type.Name);
            }

            TestSource.AssertScanned(scanned, 2, "画面を辿って入力を公開する型");

            Assert.AreEqual(0, offenders.Count,
                "親を固定していない型があります。控えの中のオブジェクトが"
                + "生の入力を読みます:" + Environment.NewLine + "  "
                + string.Join(Environment.NewLine + "  ", offenders));
        }
    }
}

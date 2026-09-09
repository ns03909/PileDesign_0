using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 下の層が画面の名前空間に頼らないこと。
    ///
    /// 入力データ (<c>Models/</c>)・解析 (<c>FEM/</c>)・サービス・出力は、画面より下にある。
    /// そこから <c>PileDesign.ViewModels</c> を使うと、
    ///
    /// <list type="bullet">
    /// <item>画面を作らずに解析だけ動かすことができなくなる</item>
    /// <item>「本当に画面が要る箇所」が、そうでない箇所に埋もれて見えなくなる</item>
    /// </list>
    ///
    /// 実際、27 ファイルが画面の名前空間を使っていたが、そのうち 17 は
    /// <c>BaseDataItem</c> / <c>BaseViewModel</c> という<b>画面固有でない土台</b>を
    /// 継承するためだけだった。土台を <c>PileDesign.Common</c> へ移して、
    /// 残ったものが本当の依存になった。
    ///
    /// <b>ここでは「増えていないこと」を見る。</b> ゼロにするには
    /// 入力データが <c>MainWindowViewModel</c> を持たない形に作り直す必要があり、
    /// 保存ファイルの形にも関わるので別の作業になる。
    /// </summary>
    [TestClass]
    public class LayerDirectionTests
    {
        /// <summary>画面より下の層。</summary>
        private static readonly string[] LowerLayers = ["Models", "FEM", "Services", "Output", "Common"];

        /// <summary>
        /// いま画面に依存しているファイル。<b>減らすのはよい。増やさないこと。</b>
        ///
        /// いずれも <c>MainWindowViewModel</c> か、画面側の描画クラスを直接使っている。
        /// 入力データの 4 つは「自分が属する入力モデルを辿る」ためだけに画面を保持しており、
        /// 本来は所属する入力モデルへの参照で足りる。
        /// </summary>
        private static readonly string[] Allowed =
        [
            "Models/InputData/InputModel.cs",
            "Models/InputData/LoadCase.cs",
            "Models/InputData/LoadCasesInput.cs",
            "Models/InputData/PileLayoutDataItem.cs",
            "Output/WordDocument.PileDiagrams.cs",
            "Output/WordDocument.cs",
            "Services/GroupSettlementExampleLoader.cs",
            "Services/MainCanvasGeometry.cs",
            "Services/PileEvaluationSummary.cs",
            "Services/PileExampleLoader.cs",
        ];

        [TestMethod]
        public void TheLowerLayers_DoNotReachForTheViewModels()
        {
            var root = TestSource.Dir("Graphics_r1");
            var offenders = new List<string>();
            int scanned = 0;

            foreach (var layer in LowerLayers)
            {
                var dir = Path.Combine(root, layer);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                    if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                    scanned++;

                    bool uses = File.ReadAllLines(file)
                        .Any(l => l.TrimStart().StartsWith("using PileDesign.ViewModels;"));
                    if (!uses) continue;

                    var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (!Allowed.Contains(rel)) offenders.Add(rel);
                }
            }

            TestSource.AssertScanned(scanned, 220, "画面より下の層のソース");

            Assert.AreEqual(0, offenders.Count,
                "下の層が画面の名前空間を使っています。土台が要るだけなら PileDesign.Common を、"
                + "本当に画面が要るなら一覧に足して理由を書いてください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
        }

        /// <summary>
        /// 一覧が古くなっていないこと。直したのに残っていると、次の人が
        /// 「まだ依存がある」と思い込む。
        /// </summary>
        [TestMethod]
        public void TheAllowedListHasNoStaleEntries()
        {
            var root = TestSource.Dir("Graphics_r1");
            var stale = Allowed
                .Where(rel =>
                {
                    var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) return true;
                    return !File.ReadAllLines(path)
                        .Any(l => l.TrimStart().StartsWith("using PileDesign.ViewModels;"));
                })
                .ToList();

            Assert.AreEqual(0, stale.Count,
                "一覧に、もう画面に依存していないファイルが残っています。外してください:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", stale));
        }

        /// <summary>
        /// 土台は画面の外にあること。ここが戻ると、また 17 ファイルが
        /// 画面の名前空間を使うことになる。
        /// </summary>
        [TestMethod]
        public void TheSharedBaseClassesLiveOutsideTheViewModels()
        {
            var root = TestSource.Dir("Graphics_r1");

            foreach (var name in new[] { "BaseDataItem.cs", "ObservableModel.cs" })
            {
                var path = Path.Combine(root, "Common", name);
                Assert.IsTrue(File.Exists(path), $"{name} が Common にありません");
                StringAssert.Contains(File.ReadAllText(path), "namespace PileDesign.Common",
                    $"{name} の名前空間が Common になっていません");
            }
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Linq;

namespace TestProject1
{
    /// <summary>
    /// 梁を消したら、どの梁からも使われなくなった<b>基礎梁節点</b>も消えること。
    ///
    /// <para>基礎梁節点は画面上で梁を引くと作られる。梁を消しても節点は残り、節点を消す操作は
    /// どこにも無かった (専用のコマンドはあったが画面から辿れず、2026-09-19 に撤去した)。
    /// そのため使われない節点がたまり続けていた。梁の削除でカスケードさせる。</para>
    ///
    /// <para>まだ使われている節点は残すこと。1 つの節点に 2 本の梁が付いているとき、
    /// 片方を消しただけで節点が消えると、残った梁の端部が行方不明になる。</para>
    /// </summary>
    [TestClass]
    public class FoundationNodeCascadeTests
    {
        /// <summary>節点 3 つ・梁 2 本 (n1-n2, n2-n3) の最小の場面。</summary>
        private static (MainWindowViewModel Vm, FoundationNode N1, FoundationNode N2, FoundationNode N3,
                        FoundationBeam B12, FoundationBeam B23) Scene()
        {
            var vm = new MainWindowViewModel();
            var model = vm.CurrentInputModel!;
            model.FoundationBeamInput ??= new FoundationBeamInput();
            var fb = model.FoundationBeamInput;

            fb.Nodes = [];
            fb.Beams = [];

            var n1 = new FoundationNode { No = 1, X = 0, Y = 0, Z = 0 };
            var n2 = new FoundationNode { No = 2, X = 5, Y = 0, Z = 0 };
            var n3 = new FoundationNode { No = 3, X = 10, Y = 0, Z = 0 };
            fb.Nodes.Add(n1);
            fb.Nodes.Add(n2);
            fb.Nodes.Add(n3);

            var b12 = new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.FoundationNode, NodeI_Id = n1.Id,
                NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = n2.Id,
            };
            var b23 = new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.FoundationNode, NodeI_Id = n2.Id,
                NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = n3.Id,
            };
            fb.Beams.Add(b12);
            fb.Beams.Add(b23);

            return (vm, n1, n2, n3, b12, b23);
        }

        [TestMethod]
        public void DeletingABeamDropsTheNodesThatNoLongerBelongToOne()
        {
            var (vm, n1, n2, n3, b12, _) = Scene();
            var fb = vm.CurrentInputModel!.FoundationBeamInput;

            vm.DeleteFoundationBeamCommand.Execute(b12);

            Assert.IsFalse(fb.Beams.Contains(b12), "梁が消えていません");
            Assert.IsFalse(fb.Nodes.Contains(n1),
                "どの梁からも使われなくなった節点が残っています (使われない節点がたまります)");
            Assert.IsTrue(fb.Nodes.Contains(n2),
                "まだ梁が付いている節点を消しました (残った梁の端部が行方不明になります)");
            Assert.IsTrue(fb.Nodes.Contains(n3), "まだ梁が付いている節点を消しました");
        }

        [TestMethod]
        public void DeletingTheLastBeamLeavesNoNodesBehind()
        {
            var (vm, _, _, _, b12, b23) = Scene();
            var fb = vm.CurrentInputModel!.FoundationBeamInput;

            vm.DeleteFoundationBeamCommand.Execute(b12);
            vm.DeleteFoundationBeamCommand.Execute(b23);

            Assert.AreEqual(0, fb.Beams.Count, "梁が残っています");
            Assert.AreEqual(0, fb.Nodes.Count,
                "梁を全部消したのに基礎梁節点が残っています");
        }

        /// <summary>残った節点の番号は 1 から振り直すこと (画面と梁の参照が番号で表示される)。</summary>
        [TestMethod]
        public void TheRemainingNodesAreRenumbered()
        {
            var (vm, _, _, _, b12, _) = Scene();
            var fb = vm.CurrentInputModel!.FoundationBeamInput;

            vm.DeleteFoundationBeamCommand.Execute(b12);

            var numbers = fb.Nodes.Select(n => n.No).ToList();
            CollectionAssert.AreEqual(Enumerable.Range(1, fb.Nodes.Count).ToList(), numbers,
                "残った節点の番号が 1 から連番になっていません: " + string.Join(",", numbers));
        }

        /// <summary>杭頭を端部に持つ梁では、杭配置の側に触らないこと。</summary>
        [TestMethod]
        public void PileHeadReferencesAreNotTouched()
        {
            var vm = new MainWindowViewModel();
            var model = vm.CurrentInputModel!;
            model.FoundationBeamInput ??= new FoundationBeamInput();
            var fb = model.FoundationBeamInput;
            fb.Nodes = [];
            fb.Beams = [];

            var node = new FoundationNode { No = 1, X = 0, Y = 0, Z = 0 };
            fb.Nodes.Add(node);

            int pilesBefore = model.PileLayoutItems?.Count ?? 0;
            var pileId = model.PileLayoutItems is { Count: > 0 } ? model.PileLayoutItems[0].UniqueId : System.Guid.NewGuid();

            var beam = new FoundationBeam
            {
                NodeI_Type = NodeReferenceType.PileLayout, NodeI_Id = pileId,
                NodeJ_Type = NodeReferenceType.FoundationNode, NodeJ_Id = node.Id,
            };
            fb.Beams.Add(beam);

            vm.DeleteFoundationBeamCommand.Execute(beam);

            Assert.AreEqual(0, fb.Nodes.Count, "基礎梁節点が残っています");
            Assert.AreEqual(pilesBefore, model.PileLayoutItems?.Count ?? 0,
                "杭配置を消しました (梁の削除で触ってよいのは基礎梁節点だけです)");
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace TestProject1
{
    [TestClass]
    public class MoveCopyValidationTests
    {
        [TestMethod]
        [DoNotParallelize]
        public async Task DecliningSplitInvalidationLeavesTheProjectUnchanged()
        {
            bool unattended = MessageService.IsUnattended;
            MessageService.IsUnattended = true;
            try
            {
                var node = new InputNode { X = 2, IsSelected = true };
                var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
                { InputNodes = [node], PileLayoutItems = [] } };
                vm.MarkProjectReplaced();
                vm.IsElementSplit = true;
                int version = vm.InputEditVersion;
                bool stale = vm.InputChangedSinceAnalysis;
                var args = new MoveCopyWindow.MoveCopyEventArgs
                { IsMove = true, DX = 5, IsInputNodesIncluded = true };
                var method = typeof(MainWindowViewModel).GetMethod("MoveCopyWindow_MoveCopyCompletedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)method.Invoke(vm, [null, args])!;
                Assert.IsTrue(args.Cancel);
                Assert.IsTrue(vm.IsElementSplit);
                Assert.IsFalse(vm.HasUnsavedWork);
                Assert.AreEqual(version, vm.InputEditVersion);
                Assert.AreEqual(stale, vm.InputChangedSinceAnalysis);
                Assert.AreEqual(2.0, node.X);
            }
            finally { MessageService.IsUnattended = unattended; }
        }

        [TestMethod]
        public async Task MovingPreservesAnalysisResultsAndRecordsTheEdit()
        {
            var node = new InputNode { X = 2, IsSelected = true };
            var input = new InputModel { InputNodes = [node], PileLayoutItems = [], PileGroupSettlement = new PileGroupSettlement() };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            vm.MarkProjectReplaced();
            vm.IsHorizontalAnalysisDone = true;
            vm.CaptureAnalysisResultSet();
            var settlementResult = input.PileGroupSettlement.Result;
            int version = vm.InputEditVersion;
            var method = typeof(MainWindowViewModel).GetMethod("MoveCopyWindow_MoveCopyCompletedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(vm, [null, new MoveCopyWindow.MoveCopyEventArgs
            { IsMove = true, DX = 5, IsInputNodesIncluded = true }])!;
            Assert.AreEqual(7.0, node.X);
            Assert.IsTrue(vm.IsHorizontalAnalysisDone);
            Assert.AreSame(settlementResult, input.PileGroupSettlement.Result);
            Assert.IsTrue(vm.HasUnsavedWork);
            Assert.IsTrue(vm.InputChangedSinceAnalysis);
            Assert.AreEqual(version + 1, vm.InputEditVersion);
        }

        [TestMethod]
        public void CopyCountRejectsOverflowAndAcceptsTheLimit()
        {
            Assert.IsNull(MoveCopyValidation.DescribeCopyCountProblem(1, MoveCopyValidation.MaxGeneratedItems));
            Assert.IsNotNull(MoveCopyValidation.DescribeCopyCountProblem(1, MoveCopyValidation.MaxGeneratedItems + 1));
            Assert.IsNotNull(MoveCopyValidation.DescribeCopyCountProblem(int.MaxValue, int.MaxValue));
            Assert.IsNotNull(MoveCopyValidation.DescribeCopyCountProblem(long.MaxValue, int.MaxValue));
            Assert.IsNotNull(MoveCopyValidation.DescribeCopyCountProblem(1, 0));
        }

        [TestMethod]
        public void InvalidPileCopyCountDoesNotChangeSelection()
        {
            var pile = new PileLayoutDataItem { IsSelected = true };
            var service = new PileLayoutService();
            Assert.ThrowsException<System.ArgumentException>(() =>
                service.CopySelectedPiles([pile], 1, 0, 0, int.MaxValue, _ => Assert.Fail()));
            Assert.IsTrue(pile.IsSelected);
        }

        [TestMethod]
        public void MissingBeamEndpointsAreReportedBeforeChangingNodes()
        {
            var node = new InputNode { X = 2, IsSelected = true };
            var beam = new FoundationBeam { IsSelected = true, NodeI_Type = NodeReferenceType.GeneralNode,
                NodeI_Id = node.UniqueId, NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = System.Guid.NewGuid() };
            var input = new InputModel { InputNodes = [node], PileLayoutItems = [],
                FoundationBeamInput = new FoundationBeamInput { Beams = [beam] } };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            foreach (bool copy in new[] { false, true })
            {
                var problem = vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs
                { IsCopy = copy, IsMove = !copy, DX = 5, RepetitionNumber = 1,
                    IsInputNodesIncluded = true, IsBeamsIncluded = true });
                Assert.IsNotNull(problem);
                StringAssert.Contains(problem, "梁 1");
                StringAssert.Contains(problem, "端点 J");
            }
            Assert.AreEqual(2.0, node.X);
            Assert.AreEqual(1, input.InputNodes.Count);
        }

        [TestMethod]
        public void BeamCopiesIncludePotentialEndpointNodesInTheLimit()
        {
            var node = new InputNode();
            var beam = new FoundationBeam { IsSelected = true, NodeI_Type = NodeReferenceType.GeneralNode,
                NodeI_Id = node.UniqueId, NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = node.UniqueId };
            var vm = new MainWindowViewModel { CurrentInputModel = new InputModel
            { InputNodes = [node], PileLayoutItems = [], FoundationBeamInput = new FoundationBeamInput { Beams = [beam] } } };
            Assert.IsNotNull(vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs
            { IsCopy = true, DX = 1, RepetitionNumber = MoveCopyValidation.MaxGeneratedItems / 3 + 1,
                IsBeamsIncluded = true }));
        }

        [TestMethod]
        public async Task CopyingOnlyNodesDoesNotCopySelectedPiles()
        {
            var pile = new PileLayoutDataItem { PileNo = 1, IsSelected = true };
            var node = new InputNode { X = 1, Type = NodeType.General, IsSelected = true };
            var input = new InputModel { PileLayoutItems = [pile], InputNodes = [node] };
            var originalPiles = input.PileLayoutItems;
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            var method = typeof(MainWindowViewModel).GetMethod("MoveCopyWindow_MoveCopyCompletedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(vm, [null, new MoveCopyWindow.MoveCopyEventArgs
            {
                IsCopy = true, DX = 5, RepetitionNumber = 2,
                IsInputNodesIncluded = true, IsPileLayoutIncluded = false, IsBeamsIncluded = false,
            }])!;
            Assert.AreSame(originalPiles, input.PileLayoutItems);
            Assert.AreEqual(1, input.PileLayoutItems.Count);
            Assert.AreEqual(3, input.InputNodes.Count);
            Assert.AreEqual(6.0, input.InputNodes[1].X);
            Assert.AreEqual(11.0, input.InputNodes[2].X);
        }
        [TestMethod]
        public void NonFiniteAndOverflowingDestinationsAreRejected()
        {
            Assert.IsNotNull(MoveCopyValidation.DescribeProblem([], double.NaN, 0, 0, 1));
            Assert.IsNotNull(MoveCopyValidation.DescribeProblem([], double.MaxValue, 0, 0, 2));
            Assert.IsNotNull(MoveCopyValidation.DescribeProblem([new Point3D(double.MaxValue, 0, 0)], double.MaxValue, 0, 0, 1));
            Assert.IsNull(MoveCopyValidation.DescribeProblem([new Point3D(1, 2, 3)], 5, 0, 0, 2));
        }

        [TestMethod]
        public async Task MovingNodesAndTheirBeamUsesTheOriginalPositions()
        {
            var a = new InputNode { X = 0, Type = NodeType.General, IsSelected = true };
            var b = new InputNode { X = 2, Type = NodeType.General, IsSelected = true };
            var input = new InputModel { InputNodes = [a, b], PileLayoutItems = [] };
            var beam = new FoundationBeam
            {
                IsSelected = true, NodeI_Type = NodeReferenceType.GeneralNode, NodeI_Id = a.UniqueId,
                NodeJ_Type = NodeReferenceType.GeneralNode, NodeJ_Id = b.UniqueId,
            };
            input.FoundationBeamInput = new FoundationBeamInput { Beams = [beam] };
            var vm = new MainWindowViewModel { CurrentInputModel = input };
            var method = typeof(MainWindowViewModel).GetMethod("MoveCopyWindow_MoveCopyCompletedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(vm, [null, new MoveCopyWindow.MoveCopyEventArgs
            {
                IsMove = true, DX = 5, IsInputNodesIncluded = true, IsBeamsIncluded = true,
            }])!;
            Assert.AreEqual(5.0, a.X);
            Assert.AreEqual(7.0, b.X);
            Assert.AreEqual(a.UniqueId, beam.NodeI_Id);
            Assert.AreEqual(b.UniqueId, beam.NodeJ_Id);
            Assert.AreEqual(2, input.InputNodes.Count);
        }
    }
}

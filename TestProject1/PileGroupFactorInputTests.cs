using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Models.InputData;

namespace TestProject1
{
    [TestClass]
    public class PileGroupFactorInputTests
    {
        [TestMethod]
        public void UndoAndRedoRecalculateTheFactor()
        {
            var main = new MainWindowViewModel { CurrentInputModel = new InputModel { PileLayoutItems = [] } };
            var vm = new GroupPileFactorViewModel(main) { TotalPileCount = 9, PileSpacingDiaRatio = 3 };
            vm.ComputePileGroupFactor();
            vm.OnApplyModelsPileNumber();
            Assert.AreEqual(0, vm.TotalPileCount);
            Assert.IsNull(vm.PileGroupFactor);
            Assert.IsTrue(vm.UndoCommand.CanExecute(null));
            vm.UndoCommand.Execute(null);
            Assert.AreEqual(9, vm.TotalPileCount);
            Assert.IsNotNull(vm.PileGroupFactor);
            Assert.IsTrue(vm.RedoCommand.CanExecute(null));
            vm.RedoCommand.Execute(null);
            Assert.AreEqual(0, vm.TotalPileCount);
            Assert.IsNull(vm.PileGroupFactor);
        }
        [TestMethod]
        public void InvalidDiameterClearsThePreviousRatioAndFactor()
        {
            var vm = new GroupPileFactorViewModel(null!)
            {
                TotalPileCount = 9, PileSpacing = 3, PileDia = 1,
            };
            vm.UpdateRatioFromDimensions();
            Assert.AreEqual(3.0, vm.PileSpacingDiaRatio);
            Assert.IsNotNull(vm.PileGroupFactor);

            vm.PileDia = 0;
            vm.UpdateRatioFromDimensions();
            Assert.IsNull(vm.PileSpacingDiaRatio);
            Assert.IsNull(vm.PileGroupFactor);
            Assert.IsFalse(string.IsNullOrEmpty(vm.ValidationMessage));
            Assert.IsNotNull(vm.UndoCommand);
            Assert.IsNotNull(vm.RedoCommand);
            Assert.IsFalse(vm.UndoCommand.CanExecute(null));
        }
        [TestMethod]
        public void InvalidInputsDoNotProduceAReusableFactor()
        {
            Assert.IsFalse(PileGroupFactor.TryGetPileGroupFactor(0, 2.5, out _));
            Assert.IsFalse(PileGroupFactor.TryGetPileGroupFactor(9, 0, out _));
            Assert.IsFalse(PileGroupFactor.TryGetPileGroupFactor(9, double.NaN, out _));
            Assert.IsFalse(PileGroupFactor.TryGetPileGroupFactor(9, double.PositiveInfinity, out _));
            Assert.IsFalse(PileGroupFactor.IsValidSpacingRatio(-1));
            Assert.IsFalse(PileGroupFactor.IsValidFactor(double.NaN));
            Assert.IsFalse(PileGroupFactor.IsValidFactor(1.1));
            Assert.IsTrue(PileGroupFactor.TryGetPileGroupFactor(9, 3, out double factor));
            Assert.IsTrue(PileGroupFactor.IsValidFactor(factor));
        }
    }
}

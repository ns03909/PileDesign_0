using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Services;
using PileDesign.ViewModels;
using PileDesign.Views;
using System;
using System.Windows.Controls;

namespace TestProject1;

[TestClass]
[DoNotParallelize]
public class MoveCopyInputTests
{
    [TestMethod]
    public void NonNumericDistanceBlocksCommandsAndRecoversAfterCorrection()
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var vm = new MainWindowViewModel();
            var field = new TextBox { DataContext = vm };
            field.AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(vm.ObserveEditDistanceInputValidation));
            field.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding("EditDistanceThreshold")
            {
                Mode = System.Windows.Data.BindingMode.TwoWay,
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
                ValidatesOnExceptions = true, NotifyOnValidationError = true,
            });
            field.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
            field.SetCurrentValue(TextBox.TextProperty, "invalid");
            field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(vm.EditDistanceInputHasError);
            Assert.IsNotNull(vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs { IsBeamsIncluded = true, IsMove = true, DX = 1 }));
            field.SetCurrentValue(TextBox.TextProperty, "0");
            field.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsFalse(vm.EditDistanceInputHasError);
            Assert.IsNull(vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs { IsBeamsIncluded = true, IsMove = true, DX = 1 }));
        }, out bool timedOut);
        Assert.IsFalse(timedOut); Assert.IsNull(error, error?.ToString());
    }

    [TestMethod]
    public void InvalidDistanceBlocksBeamEditingUntilCorrected()
    {
        var vm = new MainWindowViewModel();
        double original = vm.EditDistanceThreshold;
        foreach (double value in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => vm.EditDistanceThreshold = value);
            Assert.AreEqual(original, vm.EditDistanceThreshold);
            Assert.IsNotNull(vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs
            { IsMove = true, DX = 1, IsBeamsIncluded = true }));
        }
        vm.EditDistanceThreshold = 0;
        Assert.IsNull(vm.DescribeMoveCopyProblem(new MoveCopyWindow.MoveCopyEventArgs
        { IsMove = true, DX = 1, IsBeamsIncluded = true }));
        Assert.IsNull(MoveCopyValidation.DescribeToleranceProblem(0));
    }

    [TestMethod]
    public void SaveCommitsLatestTextAndRejectsInvalidText()
    {
        var error = XamlSmokeTestSupport.RunOnStaThread(() =>
        {
            var window = new MoveCopyWindow();
            try
            {
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
                var vm = (MoveCopyViewModel)window.DataContext;
                var x = (TextBox)window.FindName("DeltaXInput");
                var repetitions = (TextBox)window.FindName("TextBoxRepetitionNumber");
                // SetCurrentValue preserves the binding as a user edit does.
                x.SetCurrentValue(TextBox.TextProperty, "12");
                Assert.IsTrue(window.CommitNumericInputs());
                Assert.AreEqual(12.0, vm.DX);
                x.SetCurrentValue(TextBox.TextProperty, "invalid");
                Assert.IsFalse(window.CommitNumericInputs());
                Assert.AreEqual(12.0, vm.DX);
                x.SetCurrentValue(TextBox.TextProperty, "5");
                vm.IsCopySelected = true;
                foreach (string text in new[] { "0", "-1", "", "2147483648", "invalid" })
                {
                    repetitions.SetCurrentValue(TextBox.TextProperty, text);
                    Assert.IsFalse(window.CommitNumericInputs(), text);
                }
                repetitions.SetCurrentValue(TextBox.TextProperty, "3");
                Assert.IsTrue(window.CommitNumericInputs());
                Assert.AreEqual(5.0, vm.DX);
                Assert.AreEqual(3, vm.RepetitionNumber);
            }
            finally { window.Close(); }
        }, out bool timedOut);
        Assert.IsFalse(timedOut);
        Assert.IsNull(error, error?.ToString());
    }
}

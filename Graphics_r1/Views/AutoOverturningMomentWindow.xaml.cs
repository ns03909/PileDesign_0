using PileDesign.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace PileDesign.Views
{
    /// <summary>
    /// AutoOverturningMomentWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class AutoOverturningMomentWindow : Window
    {
        private readonly AutoOverturningMomentViewModel viewModel;

        public AutoOverturningMomentWindow(MainWindowViewModel mainWindowViewModel)
        {
            InitializeComponent();
            viewModel = new AutoOverturningMomentViewModel(mainWindowViewModel);
            DataContext = viewModel;
            this.Loaded += (_, _) => OkButton?.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            SetReactions();
            Close();
        }

        private void SetReactions()
        {
            for (int i = 0; i < viewModel.InputModel.LoadCasesInput.LoadCasesLevel1.Count; i++)
            {
                if (viewModel.IsApplicableE1s[i])
                {
                    var loadCase = viewModel.InputModel.LoadCasesInput.LoadCasesLevel1[i];
                    List<double> reactions = [.. viewModel.InputModel.GetReactionForUnitMoment(loadCase.LoadAngle).Select(r => r * viewModel.OverturningMoment1 * 1000)];

                    for (int j = 0; j < viewModel.InputModel.PileLayoutItems.Count; j++)
                    {
                        var pileLocation = viewModel.InputModel.PileLayoutItems[j];
                        pileLocation.AxialForceLevel1s[i] = reactions[j];

                        if (viewModel.IsApplicableVL)
                        {
                            pileLocation.AxialForceLevel1s[i] += pileLocation.AxialForceVL0;
                        }
                    }
                }
            }

            for (int i = 0; i < viewModel.InputModel.LoadCasesInput.LoadCasesLevel2.Count; i++)
            {
                if (viewModel.IsApplicableE2s[i])
                {
                    var loadCase = viewModel.InputModel.LoadCasesInput.LoadCasesLevel2[i];
                    List<double> reactions = [.. viewModel.InputModel.GetReactionForUnitMoment(loadCase.LoadAngle).Select(r => r * viewModel.OverturningMoment2 * 1000)];

                    for (int j = 0; j < viewModel.InputModel.PileLayoutItems.Count; j++)
                    {
                        var pileLocation = viewModel.InputModel.PileLayoutItems[j];
                        pileLocation.AxialForceLevel2s[i] = reactions[j];

                        if (viewModel.IsApplicableVL)
                        {
                            pileLocation.AxialForceLevel2s[i] += pileLocation.AxialForceVL0;
                        }
                    }
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            if (sender is not System.Windows.Controls.TextBox textBox) return;

            // Enter キーで Text プロパティのバインドを ViewModel 側に push し、
            // LostFocus と同じく派生プロパティ (転倒モーメント等) を再計算させる。
            var binding = BindingOperations.GetBindingExpression(textBox, System.Windows.Controls.TextBox.TextProperty);
            binding?.UpdateSource();
        }
    }
}

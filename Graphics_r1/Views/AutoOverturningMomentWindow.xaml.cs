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
            if (!CommitNumericInputs()) return;
            if (viewModel.TryApplyReactions()) Close();
        }

        internal bool CommitNumericInputs()
        {
            bool valid = true;
            void Visit(System.Windows.DependencyObject parent)
            {
                for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is System.Windows.Controls.TextBox field)
                    {
                        var binding = field.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                        if (binding?.ParentBinding.Mode != BindingMode.OneWay)
                        {
                            binding?.UpdateSource();
                            if (System.Windows.Controls.Validation.GetHasError(field)) valid = false;
                        }
                    }
                    Visit(child);
                }
            }
            Visit(this);
            return valid;
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

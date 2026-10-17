using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// 計算書を作成する前の確認 (MainWindowViewModel.ReportPreflight.cs)。
    /// 「このまま作成」・キャンセル・項目の確認先へ移る、のどれかを返す。
    /// </summary>
    public partial class ReportPreflightDialog : Window
    {
        public ReportPreflightChoice Choice { get; private set; } = ReportPreflightChoice.Cancel;
        public ReportPreflightTarget Target { get; private set; } = ReportPreflightTarget.None;

        public ReportPreflightDialog(IReadOnlyList<ReportPreflightItem> items)
        {
            InitializeComponent();
            ItemList.ItemsSource = items;
            Loaded += (_, _) => ProceedButton.Focus();
        }

        private void ProceedButton_Click(object sender, RoutedEventArgs e)
        {
            Choice = ReportPreflightChoice.Proceed;
            DialogResult = true;
        }

        private void TargetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ReportPreflightItem item })
            {
                Choice = ReportPreflightChoice.Navigate;
                Target = item.Target;
                DialogResult = false;
            }
        }

        /// <summary>確認の画面を出し、選ばれたことを返す。</summary>
        public static (ReportPreflightChoice Choice, ReportPreflightTarget Target) Ask(Window? owner, IReadOnlyList<ReportPreflightItem> items)
        {
            var dialog = new ReportPreflightDialog(items);
            if (owner != null && owner.IsVisible) dialog.Owner = owner;
            dialog.ShowDialog();
            return (dialog.Choice, dialog.Target);
        }
    }
}

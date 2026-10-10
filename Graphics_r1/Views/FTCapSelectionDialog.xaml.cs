using PileDesign.Services;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PileDesign.Views
{
    /// <summary>
    /// F.T.Pile 構法の FT キャップを選んでいない杭体を知らせ、その場で選ぶ画面 (水平解析の前に出す)。
    /// 杭径に合うキャップを最初から選んでおく。キャンセルなら解析を取りやめる。
    /// </summary>
    public partial class FTCapSelectionDialog : Window
    {
        private readonly IReadOnlyList<FTCapSelectionRow> _rows;

        public FTCapSelectionDialog(IReadOnlyList<FTCapSelectionRow> rows)
        {
            InitializeComponent();
            _rows = rows;
            DataContext = rows;
            Loaded += (_, _) => OkButton?.Focus();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var missing = _rows.Where(r => r.SelectedPhi is null).Select(r => r.PileBodyName).ToList();
            if (missing.Count > 0)
            {
                MessageService.Show($"FT キャップを選んでいない杭体があります: {string.Join("、", missing)}",
                    "FT キャップの選定", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }

        /// <summary>画面を出し、「選定して続行」なら true (行の SelectedPhi に選んだキャップが入る)。</summary>
        public static bool Select(Window? owner, IReadOnlyList<FTCapSelectionRow> rows)
        {
            var dialog = new FTCapSelectionDialog(rows) { Owner = owner };
            return dialog.ShowDialog() == true;
        }
    }
}

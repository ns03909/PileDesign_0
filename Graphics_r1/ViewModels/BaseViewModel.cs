using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    [Serializable]
    public partial class BaseViewModel : ObservableObject
    {
        // ObservableObject の OnPropertyChanged を呼び出す
        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            base.OnPropertyChanged(propertyName);

        // SetProperty は自前で保持しつつ、通知は base.OnPropertyChanged に委譲
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            base.OnPropertyChanged(propertyName);
            return true;
        }

        // --- 追加: 数値バリデーション支援 ---

        protected bool SetFiniteDouble(
            ref double field,
            double value,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = fallback;
            return SetProperty(ref field, value, propertyName);
        }

        protected bool SetFiniteClampedDouble(
            ref double field,
            double value,
            double min = double.NegativeInfinity,
            double max = double.PositiveInfinity,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = fallback;
            }
            if (value < min) value = min;
            if (value > max) value = max;
            return SetProperty(ref field, value, propertyName);
        }

        public ICommand CopyDataGridSelectionCommand { get; } = new RelayCommand(p =>
        {
            if (p is DataGrid dg) CopyDataGridSelection(dg);
        });

        private static void CopyDataGridSelection(DataGrid dataGrid)
        {
            if (dataGrid == null || dataGrid.SelectedCells.Count == 0) return;

            var sb = new StringBuilder();
            var selectedCells = dataGrid.SelectedCells.GroupBy(cell => cell.Item).ToList();

            foreach (var row in selectedCells)
            {
                var rowValues = new List<string>();
                foreach (var cell in row)
                {
                    rowValues.Add(Output.DataGridCsv.GetCellValue(cell));
                }
                sb.AppendLine(string.Join("\t", rowValues));
            }
            Clipboard.SetText(sb.ToString());
        }
    }
}
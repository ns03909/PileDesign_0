using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PileDesign.ViewModels;

namespace PileDesign.Views
{
    public partial class TableWindow : Window
    {
        public TableWindow()
        {
            InitializeComponent();
            DataContextChanged += TableWindow_DataContextChanged;
        }

        private void TableWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is TableWindowViewModel oldVm)
                oldVm.PropertyChanged -= Vm_PropertyChanged;

            if (e.NewValue is TableWindowViewModel newVm)
                newVm.PropertyChanged += Vm_PropertyChanged;

            RebuildColumns();
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TableWindowViewModel.SelectedTable))
                RebuildColumns();
        }

        private void ResultGrid_Loaded(object sender, RoutedEventArgs e) => RebuildColumns();
        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RebuildColumns();

        private void RebuildColumns()
        {
            if (DataContext is not TableWindowViewModel vm) return;

            ResultGrid.Columns.Clear();
            if (vm.SelectedTable == null) return;

            // メタ列（DataGrid の DataContext を起点に SelectedTable を参照）
            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "荷重条件",
                Binding = new Binding("DataContext.SelectedTable.LoadCaseName")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
                },
                IsReadOnly = true
            });

            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "荷重組合せ",
                Binding = new Binding("DataContext.SelectedTable.LoadCombinationName")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
                },
                IsReadOnly = true
            });

            ResultGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "液状化",
                Binding = new Binding("DataContext.SelectedTable.LiquefactionLabel")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1)
                },
                IsReadOnly = true
            });

            // 動的列
            foreach (var col in vm.SelectedTable.Columns)
            {
                ResultGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = col.Header,
                    Binding = new Binding(col.Property.Name)
                    {
                        StringFormat = col.Format
                    },
                    IsReadOnly = true
                });
            }
        }
    }
}
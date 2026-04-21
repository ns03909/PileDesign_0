using PileDesign.Output;
using PileDesign.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

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
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGrid), 1),
                    Mode = BindingMode.OneWay
                },
                IsReadOnly = true
            });

            // 動的列
            foreach (var col in vm.SelectedTable.Columns)
            {
                object header = col.Header;
                if (!string.IsNullOrEmpty(col.Tooltip))
                {
                    header = new System.Windows.Controls.TextBlock
                    {
                        Text = col.Header,
                        ToolTip = col.Tooltip
                    };
                }

                var column = new DataGridTextColumn
                {
                    Header = header,
                    Binding = new Binding(col.Property.Name)
                    {
                        StringFormat = col.Format
                    },
                    IsReadOnly = true
                };

                // 数値型の列は右揃えにする
                if (IsNumericType(col.Property.PropertyType))
                    column.ElementStyle = _rightAlignTextStyle;

                ResultGrid.Columns.Add(column);
            }
        }

        private static readonly Style _rightAlignTextStyle = BuildRightAlignStyle();

        private static Style BuildRightAlignStyle()
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 0, 4, 0)));
            return style;
        }

        private static bool IsNumericType(Type t)
        {
            if (t == null) return false;
            t = Nullable.GetUnderlyingType(t) ?? t;
            return t == typeof(double) || t == typeof(float) || t == typeof(decimal)
                || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(sbyte)
                || t == typeof(uint) || t == typeof(ulong) || t == typeof(ushort) || t == typeof(byte);
        }

        // CSVエクスポートのコンテキストメニュークリックイベントハンドラ
        private void ExportCsvFromContextMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.CommandParameter is DataGrid dataGrid)
            {
                var data = dataGrid.ItemsSource.Cast<object>();
                {
                    DataGridCsv.Export(data, dataGrid);
                }
            }
        }

        // ContextMenuが開かれたときにDataGridをCommandParameterに設定するイベントハンドラ
        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is ContextMenu contextMenu)
            {
                if (contextMenu.PlacementTarget is DataGrid dataGrid)
                {
                    foreach (MenuItem menuItem in contextMenu.Items)
                    {
                        menuItem.CommandParameter = dataGrid;
                    }
                }
            }
        }
    }
}
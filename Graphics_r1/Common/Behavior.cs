using Microsoft.Xaml.Behaviors;
using System;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PileDesign.Common
{
    [SupportedOSPlatform("windows7.0")]
    public class DataGridSingleClickEditBehavior : Behavior<EnhancedDataGrid>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            if (AssociatedObject != null)
            {
                AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            }
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            if (AssociatedObject != null)
            {
                AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
            }
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var cell = FindParent<DataGridCell>(e.OriginalSource as DependencyObject);
            if (cell != null && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                    cell.Focus();

                var dataGrid = AssociatedObject;
                if (dataGrid != null)
                {
                    dataGrid.SelectedItem = cell.DataContext;
                    dataGrid.CurrentCell = new DataGridCellInfo(cell);
                    dataGrid.BeginEdit();
                    e.Handled = true;
                }
            }
        }

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T target) return target;

                // VisualTreeHelper.GetParent は Visual ではない要素（Run など）で例外を投げるためガードが必要
                if (child is Visual or Visual3D)
                {
                    child = VisualTreeHelper.GetParent(child);
                }
                // Run などの FrameworkContentElement の場合は論理ツリーの Parent を参照する
                else if (child is FrameworkContentElement fce)
                {
                    child = fce.Parent;
                }
                else
                {
                    child = null;
                }
            }
            return null;
        }
    }
}

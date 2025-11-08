using Microsoft.Xaml.Behaviors;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PileDesign.Common
{
    public class DataGridSingleClickEditBehavior : Behavior<EnhancedDataGrid>
    {
        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
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

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            if (child == null) return null;

            if (child is T parent) return parent;

            return FindParent<T>(VisualTreeHelper.GetParent(child));
        }
    }
}

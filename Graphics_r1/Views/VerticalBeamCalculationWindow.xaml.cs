using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    public partial class VerticalBeamCalculationWindow : Window
    {
        public VerticalBeamCalculationWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
                vm.RequestClose += (_, __) => Close();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm && vm.OkCommand?.CanExecute(null) == true)
                {
                    vm.OkCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F9)
            {
                if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm2 && vm2.ExecuteAnalysisCommand?.CanExecute(null) == true)
                {
                    vm2.ExecuteAnalysisCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm)
                {
                    if (vm.CancelCommand?.CanExecute(null) == true)
                        vm.CancelCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void CalculationLog_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogListBox.Items.Count > 0)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    LogListBox.ScrollIntoView(LogListBox.Items[^1]);
                });
            }
        }

    }
}

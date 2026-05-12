using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;

namespace PileDesign.Views
{
    public partial class GroupSettlementWithBeamWindow : Window
    {
        public GroupSettlementWithBeamWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            PreviewKeyDown += Window_PreviewKeyDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.GroupSettlementWithBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
                vm.RequestClose += OnVmRequestClose;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.GroupSettlementWithBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
                vm.RequestClose -= OnVmRequestClose;
            }
        }

        private void OnVmRequestClose(object sender, EventArgs e) => Close();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.GroupSettlementWithBeamCalculationViewModel vm) return;
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (vm.OkCommand?.CanExecute(null) == true) vm.OkCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.F9)
            {
                if (vm.ExecuteAnalysisCommand?.CanExecute(null) == true) vm.ExecuteAnalysisCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (vm.CancelCommand?.CanExecute(null) == true) vm.CancelCommand.Execute(null);
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

using System.Collections.Specialized;
using System.Text.RegularExpressions;
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
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged += CalculationLog_CollectionChanged;
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.VerticalBeamCalculationViewModel vm)
            {
                vm.CalculationLog.CollectionChanged -= CalculationLog_CollectionChanged;
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

        private void NumericOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9eE.\-+]$");
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}

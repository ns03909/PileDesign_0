using System;
using System.Windows;

namespace PileDesignCore
{
    public partial class AxialForceWindow : Window
    {
        private readonly AxialForceViewModel viewModel;

        public AxialForceWindow(ApplicationViewModel appViewModel)
        {
            InitializeComponent();
            viewModel = new AxialForceViewModel
            {
                LoadCases = appViewModel.LoadCaseViewModel.AllLoadCases
            };
            DataContext = viewModel;
        }

        public class AxialForceEventArgs : EventArgs
        {
            public bool IsReplace { get; set; } = true;
            public bool IsAdd { get; set; } = false;
            public double AxialForce { get; set; } = 0;
            public string SelectedLoadCase { get; set; } = "VL";
        }

        public event EventHandler<AxialForceEventArgs> AxialForceCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            AxialForceEventArgs args = new AxialForceEventArgs
            {
                IsReplace = viewModel.IsReplaceValueMode,
                IsAdd = viewModel.IsAddValueMode,
                AxialForce = viewModel.AxialForce,
                SelectedLoadCase = viewModel.SelectedLoadCase
            };

            AxialForceCompleted?.Invoke(this, args);
            viewModel.ResetStatus();
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            viewModel.ResetStatus();
            Close();
        }
    }
}

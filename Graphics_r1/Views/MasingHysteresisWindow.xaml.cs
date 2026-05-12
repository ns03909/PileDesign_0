using PileDesign.ViewModels;
using System;
using System.Windows;

namespace PileDesign.Views
{
    public partial class MasingHysteresisWindow : Window
    {
        private EventHandler _requestCloseHandler;

        public MasingHysteresisWindow()
        {
            InitializeComponent();
            Loaded += MasingHysteresisWindow_Loaded;
            Closed += MasingHysteresisWindow_Closed;
        }

        public MasingHysteresisWindow(MasingHysteresisViewModel vm) : this()
        {
            DataContext = vm;
        }

        private void MasingHysteresisWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MasingHysteresisViewModel vm) return;
            vm.WpfPlotHead = WpfPlotHead;
            vm.WpfPlotToe = WpfPlotToe;
            _requestCloseHandler = (s, args) =>
            {
                if (this.IsLoaded && this.IsVisible) this.Close();
            };
            vm.RequestClose += _requestCloseHandler;
            vm.UpdatePlots();
        }

        private void MasingHysteresisWindow_Closed(object sender, EventArgs e)
        {
            if (DataContext is MasingHysteresisViewModel vm && _requestCloseHandler != null)
            {
                vm.RequestClose -= _requestCloseHandler;
                _requestCloseHandler = null;
            }
        }
    }
}

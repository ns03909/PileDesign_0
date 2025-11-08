using System;
using System.Windows;

namespace PileDesignCore
{
    /// <summary>
    /// EditPileLayoutWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class EditPileLayoutWindow : Window
    {
        private readonly EditPileLayoutViewModel viewModel;

        public EditPileLayoutWindow(ApplicationViewModel appViewModel)
        {
            InitializeComponent();
            viewModel = new EditPileLayoutViewModel();
            DataContext = viewModel;
        }

        public class EditPileLayoutEventArgs : EventArgs
        {
            public bool IsApplicablePileRefNo = false;
            public int SelectedPileRefNo = 1;

            public bool IsApplicableGroundRefNo = false;
            public int SelectedGroundRefNo = 1;

            public bool IsApplicablePileTopLevel = false;
            public double PileTopLevel;
            public bool IsReplacePileTopLevel = true;
            public bool IsAddPileTopLevel = false;

            public bool IsApplicablePileGroupFactor = false;
            public double PileGroupFactor = 1;
            public bool IsReplacePileGroupFactor = true;
            public bool IsAddPileGroupFactor = false;

            public bool IsApplicableVL = false;
            public double VL = 0;
            public bool IsReplaceVL = true;
            public bool IsAddVL = false;

            public bool IsApplicableVLadd = false;
            public double VLadd = 0;
            public bool IsReplaceVLadd = true;
            public bool IsAddVLadd = false;

            public bool IsApplicableE1 = false;
            public double E1 = 0;
            public bool IsReplaceE1 = true;
            public bool IsAddE1 = false;

            public bool IsApplicableE2 = false;
            public double E2 = 0;
            public bool IsReplaceE2 = true;
            public bool IsAddE2 = false;

            public bool IsApplicableE1_1 = false;
            public double E1_1 = 0;
            public bool IsReplaceE1_1 = true;
            public bool IsAddE1_1 = false;

            public bool IsApplicableE1_2 = false;
            public double E1_2 = 0;
            public bool IsReplaceE1_2 = true;
            public bool IsAddE1_2 = false;

            public bool IsApplicableE1_3 = false;
            public double E1_3 = 0;
            public bool IsReplaceE1_3 = true;
            public bool IsAddE1_3 = false;

            public bool IsApplicableE1_4 = false;
            public double E1_4 = 0;
            public bool IsReplaceE1_4 = true;
            public bool IsAddE1_4 = false;

            public bool IsApplicableE2_1 = false;
            public double E2_1 = 0;
            public bool IsReplaceE2_1 = true;
            public bool IsAddE2_1 = false;

            public bool IsApplicableE2_2 = false;
            public double E2_2 = 0;
            public bool IsReplaceE2_2 = true;
            public bool IsAddE2_2 = false;

            public bool IsApplicableE2_3 = false;
            public double E2_3 = 0;
            public bool IsReplaceE2_3 = true;
            public bool IsAddE2_3 = false;

            public bool IsApplicableE2_4 = false;
            public double E2_4 = 0;
            public bool IsReplaceE2_4 = true;
            public bool IsAddE2_4 = false;

            public bool IsApplicableIsFrontPile1 = false;
            public bool IsFrontPile1 = true;
            public bool IsBackPile1 = false;

            public bool IsApplicableIsFrontPile2 = false;
            public bool IsFrontPile2 = true;
            public bool IsBackPile2 = false;

            public bool IsApplicableIsFrontPile3 = false;
            public bool IsFrontPile3 = true;
            public bool IsBackPile3 = false;

            public bool IsApplicableIsFrontPile4 = false;
            public bool IsFrontPile4 = true;
            public bool IsBackPile4 = false;

        }

        public event EventHandler<EditPileLayoutEventArgs> EditPileLayoutCompleted;

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            EditPileLayoutEventArgs args = new EditPileLayoutEventArgs
            {
                IsApplicablePileRefNo = viewModel.IsApplicablePileRefNo,
                SelectedPileRefNo = viewModel.SelectedPileRefNo,

                IsApplicableGroundRefNo = viewModel.IsApplicableGroundRefNo,
                SelectedGroundRefNo = viewModel.SelectedGroundRefNo,

                IsApplicablePileTopLevel = viewModel.IsApplicablePileTopLevel,
                PileTopLevel = viewModel.PileTopLevel,
                IsReplacePileTopLevel = viewModel.IsReplacePileTopLevel,
                IsAddPileTopLevel = viewModel.IsAddPileTopLevel,

                IsApplicablePileGroupFactor = viewModel.IsApplicablePileGroupFactor,
                PileGroupFactor = viewModel.PileGroupFactor,
                IsReplacePileGroupFactor = viewModel.IsReplacePileGroupFactor,
                IsAddPileGroupFactor = viewModel.IsAddPileGroupFactor,

                IsApplicableVL = viewModel.IsApplicableVL,
                VL = viewModel.VL,
                IsReplaceVL = viewModel.IsReplaceVL,
                IsAddVL = viewModel.IsAddVL,

                IsApplicableVLadd = viewModel.IsApplicableVLadd,
                VLadd = viewModel.VLadd,
                IsReplaceVLadd = viewModel.IsReplaceVLadd,
                IsAddVLadd = viewModel.IsAddVLadd,

                IsApplicableE1 = viewModel.IsApplicableE1,
                E1 = viewModel.E1,
                IsReplaceE1 = viewModel.IsReplaceE1,
                IsAddE1 = viewModel.IsAddE1,

                IsApplicableE2 = viewModel.IsApplicableE2,
                E2 = viewModel.E2,
                IsReplaceE2 = viewModel.IsReplaceE2,
                IsAddE2 = viewModel.IsAddE2,

                IsApplicableE1_1 = viewModel.IsApplicableE1_1,
                E1_1 = viewModel.E1_1,
                IsReplaceE1_1 = viewModel.IsReplaceE1_1,
                IsAddE1_1 = viewModel.IsAddE1_1,

                IsApplicableE1_2 = viewModel.IsApplicableE1_2,
                E1_2 = viewModel.E1_2,
                IsReplaceE1_2 = viewModel.IsReplaceE1_2,
                IsAddE1_2 = viewModel.IsAddE1_2,

                IsApplicableE1_3 = viewModel.IsApplicableE1_3,
                E1_3 = viewModel.E1_3,
                IsReplaceE1_3 = viewModel.IsReplaceE1_3,
                IsAddE1_3 = viewModel.IsAddE1_3,

                IsApplicableE1_4 = viewModel.IsApplicableE1_4,
                E1_4 = viewModel.E1_4,
                IsReplaceE1_4 = viewModel.IsReplaceE1_4,
                IsAddE1_4 = viewModel.IsAddE1_4,

                IsApplicableE2_1 = viewModel.IsApplicableE2_1,
                E2_1 = viewModel.E2_1,
                IsReplaceE2_1 = viewModel.IsReplaceE2_1,
                IsAddE2_1 = viewModel.IsAddE2_1,

                IsApplicableE2_2 = viewModel.IsApplicableE2_2,
                E2_2 = viewModel.E2_2,
                IsReplaceE2_2 = viewModel.IsReplaceE2_2,
                IsAddE2_2 = viewModel.IsAddE2_2,

                IsApplicableE2_3 = viewModel.IsApplicableE2_3,
                E2_3 = viewModel.E2_3,
                IsReplaceE2_3 = viewModel.IsReplaceE2_3,
                IsAddE2_3 = viewModel.IsAddE2_3,

                IsApplicableE2_4 = viewModel.IsApplicableE2_4,
                E2_4 = viewModel.E2_4,
                IsReplaceE2_4 = viewModel.IsReplaceE2_4,
                IsAddE2_4 = viewModel.IsAddE2_4,

                IsApplicableIsFrontPile1 = viewModel.IsApplicableIsFrontPile1,
                IsFrontPile1 = viewModel.IsFrontPile1,
                IsBackPile1 = viewModel.IsBackPile1,

                IsApplicableIsFrontPile2 = viewModel.IsApplicableIsFrontPile2,
                IsFrontPile2 = viewModel.IsFrontPile2,
                IsBackPile2 = viewModel.IsBackPile2,

                IsApplicableIsFrontPile3 = viewModel.IsApplicableIsFrontPile3,
                IsFrontPile3 = viewModel.IsFrontPile3,
                IsBackPile3 = viewModel.IsBackPile3,

                IsApplicableIsFrontPile4 = viewModel.IsApplicableIsFrontPile4,
                IsFrontPile4 = viewModel.IsFrontPile4,
                IsBackPile4 = viewModel.IsBackPile4,
    };

            EditPileLayoutCompleted?.Invoke(this, args);
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

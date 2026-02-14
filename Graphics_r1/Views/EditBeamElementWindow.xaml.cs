using PileDesign.ViewModels;
using System;
using System.Windows;

namespace PileDesign.Views
{
    public partial class EditBeamElementWindow : Window
    {
        private readonly EditBeamElementViewModel viewModel;

        public EditBeamElementWindow(EditBeamElementViewModel viewModel)
        {
            InitializeComponent();
            this.viewModel = viewModel;
            DataContext = viewModel;
        }

        public class BeamElementEditResult
        {
            public bool IsApplicableMaterialNo { get; set; }
            public int? MaterialNo { get; set; }
            public bool IsApplicableSectionNo { get; set; }
            public int? SectionNo { get; set; }
        }

        public BeamElementEditResult Result { get; private set; }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Result = new BeamElementEditResult
            {
                IsApplicableMaterialNo = viewModel.IsApplicableMaterialNo,
                MaterialNo = viewModel.SelectedMaterial?.No,
                IsApplicableSectionNo = viewModel.IsApplicableSectionNo,
                SectionNo = viewModel.SelectedSection?.No
            };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

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
            // No プロパティ廃止により、コレクション内位置 (1-based) を計算する
            int? matNo = viewModel.SelectedMaterial != null && viewModel.Materials != null
                ? viewModel.Materials.IndexOf(viewModel.SelectedMaterial) + 1
                : null;
            int? secNo = viewModel.SelectedSection != null && viewModel.Sections != null
                ? viewModel.Sections.IndexOf(viewModel.SelectedSection) + 1
                : null;

            Result = new BeamElementEditResult
            {
                IsApplicableMaterialNo = viewModel.IsApplicableMaterialNo,
                MaterialNo = matNo,
                IsApplicableSectionNo = viewModel.IsApplicableSectionNo,
                SectionNo = secNo
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

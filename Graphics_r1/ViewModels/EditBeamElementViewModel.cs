using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    public partial class EditBeamElementViewModel : BaseViewModel
    {
        [ObservableProperty]
        private string _selectionInfo;

        // 材料
        [ObservableProperty]
        private ObservableCollection<BeamMaterial> _materials;

        [ObservableProperty]
        private BeamMaterial _selectedMaterial;

        [ObservableProperty]
        private bool _isApplicableMaterialNo;

        // 断面
        [ObservableProperty]
        private ObservableCollection<BeamSection> _sections;

        [ObservableProperty]
        private BeamSection _selectedSection;

        [ObservableProperty]
        private bool _isApplicableSectionNo;

        public EditBeamElementViewModel(int selectedCount, ObservableCollection<BeamMaterial> materials, ObservableCollection<BeamSection> sections)
        {
            SelectionInfo = $"選択された一般梁要素: {selectedCount}個";
            Materials = materials;
            Sections = sections;

            // デフォルトで最初の要素を選択
            if (materials != null && materials.Count > 0)
            {
                SelectedMaterial = materials[0];
            }

            if (sections != null && sections.Count > 0)
            {
                SelectedSection = sections[0];
            }
        }
    }
}

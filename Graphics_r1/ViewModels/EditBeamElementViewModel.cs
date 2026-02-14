using PileDesign.Models.InputData;
using System.Collections.ObjectModel;

namespace PileDesign.ViewModels
{
    public class EditBeamElementViewModel : BaseViewModel
    {
        private string _selectionInfo;
        public string SelectionInfo
        {
            get => _selectionInfo;
            set => SetProperty(ref _selectionInfo, value);
        }

        // 材料
        private ObservableCollection<BeamMaterial> _materials;
        public ObservableCollection<BeamMaterial> Materials
        {
            get => _materials;
            set => SetProperty(ref _materials, value);
        }

        private BeamMaterial _selectedMaterial;
        public BeamMaterial SelectedMaterial
        {
            get => _selectedMaterial;
            set => SetProperty(ref _selectedMaterial, value);
        }

        private bool _isApplicableMaterialNo;
        public bool IsApplicableMaterialNo
        {
            get => _isApplicableMaterialNo;
            set => SetProperty(ref _isApplicableMaterialNo, value);
        }

        // 断面
        private ObservableCollection<BeamSection> _sections;
        public ObservableCollection<BeamSection> Sections
        {
            get => _sections;
            set => SetProperty(ref _sections, value);
        }

        private BeamSection _selectedSection;
        public BeamSection SelectedSection
        {
            get => _selectedSection;
            set => SetProperty(ref _selectedSection, value);
        }

        private bool _isApplicableSectionNo;
        public bool IsApplicableSectionNo
        {
            get => _isApplicableSectionNo;
            set => SetProperty(ref _isApplicableSectionNo, value);
        }

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

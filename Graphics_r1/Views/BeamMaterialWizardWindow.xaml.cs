using PileDesign.Models.InputData;
using PileDesign.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PileDesign.Services;

namespace PileDesign.Views
{
    public partial class BeamMaterialWizardWindow : Window
    {
        private readonly BeamMaterialWizardViewModel viewModel;
        private readonly ObservableCollection<BeamMaterial> materials;

        public BeamMaterialWizardWindow(ObservableCollection<BeamMaterial> materials)
        {
            InitializeComponent();
            viewModel = new BeamMaterialWizardViewModel();
            DataContext = viewModel;

            this.materials = materials;

            // ComboBoxに「新規」と既存材料Noを設定
            var items = new List<object> { "新規" };
            items.AddRange(materials);
            ComboBoxMaterialNo.ItemsSource = items;
            ComboBoxMaterialNo.DisplayMemberPath = "No";

            // デフォルトで「新規」を選択
            ComboBoxMaterialNo.SelectedIndex = 0;
        }

        public class MaterialWizardResult
        {
            public string Name { get; set; }
            public double YoungModulus { get; set; }  // kN/m²
            public double ShearModulus { get; set; }  // kN/m²
            public double PoissonRatio { get; set; }
        }

        public MaterialWizardResult Result { get; private set; }
        public int? SelectedMaterialNo { get; private set; }  // null = 新規
        public bool IsApplyClicked { get; private set; }

        private void ComboBoxMaterialNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxMaterialNo.SelectedItem is BeamMaterial material)
            {
                // 既存材料が選択された場合、値をViewModelに反映
                viewModel.Name = material.Name;
                viewModel.PoissonRatio = material.PoissonRatio;
                viewModel.Gamma = 24.0; // デフォルト値

                // Ec (N/mm²) = 3.35e4 × (γ/24)² × (Fc/60)^(1/3) の逆算
                // Fc = 60 × [Ec / (3.35e4 × (γ/24)²)]³
                double ecNmm2 = material.YoungModulus / 1000.0; // kN/m² → N/mm²
                double gammaRatio = viewModel.Gamma / 24.0;
                double ratio = ecNmm2 / (3.35e4 * gammaRatio * gammaRatio);
                viewModel.Fc = 60.0 * ratio * ratio * ratio;
            }
            else if (ComboBoxMaterialNo.SelectedItem is string str && str == "新規")
            {
                // 「新規」が選択された場合、デフォルト値を設定
                viewModel.Name = "C24";
                viewModel.Fc = 24.0;
                viewModel.Gamma = 24.0;
                viewModel.PoissonRatio = 0.2;
            }
        }

        private bool PrepareResult()
        {
            if (ComboBoxMaterialNo.SelectedItem == null)
            {
                MessageService.Show("材料を選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            // 「新規」または既存材料の場合
            if (ComboBoxMaterialNo.SelectedItem is BeamMaterial material)
            {
                SelectedMaterialNo = material.No;
            }
            else
            {
                SelectedMaterialNo = null; // 新規
            }

            // 計算結果を kN/m² で返す
            Result = new MaterialWizardResult
            {
                Name = viewModel.Name,
                YoungModulus = viewModel.Ec * 1000,  // N/mm² → kN/m²
                ShearModulus = viewModel.G * 1000,   // N/mm² → kN/m²
                PoissonRatio = viewModel.PoissonRatio
            };

            return true;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrepareResult()) return;

            IsApplyClicked = true;
            DialogResult = true;
            IsApplyClicked = false; // リセット（次回の適用のため）

            // 新規の場合は、追加された材料を選択状態にする
            // （MainWindowViewModelで処理後、この情報が必要）
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrepareResult()) return;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            textBox?.SelectAll();
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                e.Handled = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ViewModelの計算が自動的に行われる
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelButton_Click(null, new RoutedEventArgs());
            }
        }

        public void SelectMaterial(BeamMaterial material)
        {
            // ComboBoxで該当する材料を選択
            ComboBoxMaterialNo.SelectedItem = material;
        }
    }
}

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
    public partial class BeamSectionWizardWindow : Window
    {
        private readonly BeamSectionWizardViewModel viewModel;
        private readonly ObservableCollection<BeamSection> sections;

        // ComboBox 用ラッパー: 既存断面行 (位置 = 1-based No) と末尾の "(New)" 行を 1 つの型で扱う
        // 地盤ウィンドウ・杭体ウィンドウと同じスタイル: "1", "2", ..., "N+1 (New)"
        private sealed class SectionPickerItem
        {
            public string Display { get; }
            public BeamSection? Section { get; }
            public bool IsNew => Section == null;
            public SectionPickerItem(string display, BeamSection? section) { Display = display; Section = section; }
            public override string ToString() => Display;
        }

        public BeamSectionWizardWindow(ObservableCollection<BeamSection> sections)
        {
            InitializeComponent();
            viewModel = new BeamSectionWizardViewModel();
            DataContext = viewModel;

            this.sections = sections;

            // 既存断面 (1-based 位置 = No) → 末尾に "(New)" 行
            var items = new List<SectionPickerItem>();
            for (int i = 0; i < sections.Count; i++)
                items.Add(new SectionPickerItem($"{i + 1}", sections[i]));
            items.Add(new SectionPickerItem($"{sections.Count + 1} (New)", null));
            ComboBoxSectionNo.ItemsSource = items;
            ComboBoxSectionNo.DisplayMemberPath = "Display";

            // デフォルトで先頭 (=「1」) を選択。既存が無ければ "(New)" のみで自動的に "(New)" が選択される。
            ComboBoxSectionNo.SelectedIndex = 0;
        }

        public class SectionWizardResult
        {
            public string Name { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public double Area { get; set; }
            public double ShearAreaY { get; set; }
            public double ShearAreaZ { get; set; }
            public double TorsionalMoment { get; set; }
            public double MomentOfInertiaYY { get; set; }
            public double MomentOfInertiaZZ { get; set; }
            // 増減係数
            public double AFactor { get; set; }
            public double AyFactor { get; set; }
            public double AzFactor { get; set; }
            public double IxxFactor { get; set; }
            public double IyyFactor { get; set; }
            public double IzzFactor { get; set; }
        }

        public SectionWizardResult Result { get; private set; }
        public int? SelectedSectionNo { get; private set; }  // null = 新規
        public bool IsApplyClicked { get; private set; }

        private void ComboBoxSectionNo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxSectionNo.SelectedItem is not SectionPickerItem picker) return;

            if (picker.Section is BeamSection section)
            {
                // 既存断面が選択された場合、値をViewModelに反映
                viewModel.Name = section.Name;
                viewModel.Width = section.Width;
                viewModel.Height = section.Height;

                // 増減係数をBeamSectionから復元
                viewModel.AFactor = section.AFactor;
                viewModel.AyFactor = section.AyFactor;
                viewModel.AzFactor = section.AzFactor;
                viewModel.IxxFactor = section.IxxFactor;
                viewModel.IyyFactor = section.IyyFactor;
                viewModel.IzzFactor = section.IzzFactor;
            }
            else
            {
                // 「新規」が選択された場合、デフォルト値を設定
                viewModel.Name = "500x800";
                viewModel.Width = 0.5;
                viewModel.Height = 0.8;
                viewModel.AFactor = 1.0;
                viewModel.AyFactor = 1.0;
                viewModel.AzFactor = 1.0;
                viewModel.IxxFactor = 1.0;
                viewModel.IyyFactor = 1.0;
                viewModel.IzzFactor = 1.0;
            }
        }

        private bool PrepareResult()
        {
            if (ComboBoxSectionNo.SelectedItem == null)
            {
                MessageService.Show("断面を選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            // 「新規」または既存断面の場合 (No = コレクション内の 1-based 位置インデックス)
            if (ComboBoxSectionNo.SelectedItem is SectionPickerItem picker && picker.Section is BeamSection section)
            {
                SelectedSectionNo = sections.IndexOf(section) + 1;
            }
            else
            {
                SelectedSectionNo = null; // 新規
            }

            Result = new SectionWizardResult
            {
                Name = viewModel.Name,
                Width = viewModel.Width,
                Height = viewModel.Height,
                Area = viewModel.Area,
                ShearAreaY = viewModel.ShearAreaY,
                ShearAreaZ = viewModel.ShearAreaZ,
                TorsionalMoment = viewModel.TorsionalMoment,
                MomentOfInertiaYY = viewModel.MomentOfInertiaYY,
                MomentOfInertiaZZ = viewModel.MomentOfInertiaZZ,
                AFactor = viewModel.AFactor,
                AyFactor = viewModel.AyFactor,
                AzFactor = viewModel.AzFactor,
                IxxFactor = viewModel.IxxFactor,
                IyyFactor = viewModel.IyyFactor,
                IzzFactor = viewModel.IzzFactor
            };

            return true;
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!PrepareResult()) return;

            IsApplyClicked = true;
            DialogResult = true;
            IsApplyClicked = false; // リセット（次回の適用のため）
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

        public void SelectSection(BeamSection section)
        {
            // ComboBoxで該当する断面を選択 (PickerItem 経由)
            if (ComboBoxSectionNo.ItemsSource is IEnumerable<SectionPickerItem> items)
            {
                ComboBoxSectionNo.SelectedItem = items.FirstOrDefault(it => it.Section == section);
            }
        }
    }
}

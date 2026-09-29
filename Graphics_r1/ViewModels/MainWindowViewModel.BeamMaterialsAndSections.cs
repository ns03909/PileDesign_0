using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Constants;
using PileDesign.FEM;
using PileDesign.Models.InputData;
using PileDesign.Models.Results;
using PileDesign.Services;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using static PileDesign.Views.AutoIsFrontPilesWindow;
using static PileDesign.Views.EditPileLayoutWindow;
using static PileDesign.Views.MoveCopyWindow;
using Point = System.Windows.Point;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

using Serilog;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// MainWindowViewModel — 基礎梁の材料と断面。
    ///
    /// 材料・断面をウィザードで足し、消し、番号を振り直す。
    ///
    /// <b>消したら参照も直すこと。</b> 材料と断面は梁要素から番号で参照している。
    /// 消して詰めるだけだと、番号がずれて<b>別の材料を指したまま解析が通る</b>。
    /// 消したあとに必ず番号を振り直し、梁要素側の参照も書き換える
    /// (<see cref="RenumberMaterialsAndUpdateReferences"/> /
    ///  <see cref="RenumberSectionsAndUpdateReferences"/>)。
    ///
    /// 以前は <c>MainWindowViewModel.cs</c> (4,251 行) の中にあった。
    /// </summary>
    public partial class MainWindowViewModel
    {
        // 材料ウィザードコマンド
        [RelayCommand]
        private void OpenMaterialWizard()
        {
            var wizard = new Views.BeamMaterialWizardWindow(
                CurrentInputModel.FoundationBeamInput?.Materials ?? new ObservableCollection<BeamMaterial>());

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    if (!ApplyBeamMaterial(wizard.SelectedMaterialNo, wizard.Result, out int? addedMaterialNo)) return;

                    // Applyボタンが押された場合は編集を継続
                    if (wizard.IsApplyClicked)
                    {
                        // 新規作成の場合、作成された材料を選択状態にする
                        if (addedMaterialNo.HasValue)
                        {
                            wizard = new Views.BeamMaterialWizardWindow(CurrentInputModel.FoundationBeamInput.Materials);
                            // 追加された材料を選択（ComboBoxのインデックスは「新規」の分だけオフセット）
                            var addedMaterial = addedMaterialNo.Value >= 1
                                ? CurrentInputModel.FoundationBeamInput.Materials.ElementAtOrDefault(addedMaterialNo.Value - 1)
                                : null;
                            if (addedMaterial != null)
                            {
                                wizard.SelectMaterial(addedMaterial);
                            }
                        }
                        continueEditing = true;
                    }
                    else
                    {
                        continueEditing = false;
                    }
                }
                else
                {
                    // キャンセルされた
                    continueEditing = false;
                }
            }
        }

        // 全断面ねじれ剛性無視コマンド
        [RelayCommand]
        private void ClearAllTorsionalStiffness()
        {
            if (CurrentInputModel?.FoundationBeamInput?.Sections == null ||
                CurrentInputModel.FoundationBeamInput.Sections.Count == 0) return;

            if (CurrentInputModel.FoundationBeamInput.Sections.All(s => s.IxxFactor == 0 && s.TorsionalMoment == 0)) return;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return;

            var before = CaptureInputEdit();

            foreach (var section in CurrentInputModel.FoundationBeamInput.Sections)
            {
                section.IxxFactor = 0.0;
                // Ixx係数=0 に合わせて TorsionalMoment も再計算
                section.TorsionalMoment = 0.0;
            }

            CompleteInputEdit(before);
        }

        // 断面ウィザードコマンド
        [RelayCommand]
        private void OpenSectionWizard()
        {
            var wizard = new Views.BeamSectionWizardWindow(
                CurrentInputModel.FoundationBeamInput?.Sections ?? new ObservableCollection<BeamSection>());

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    if (!ApplyBeamSection(wizard.SelectedSectionNo, wizard.Result, out int? addedSectionNo)) return;

                    // Applyボタンが押された場合は編集を継続
                    if (wizard.IsApplyClicked)
                    {
                        // 新規作成の場合、作成された断面を選択状態にする
                        if (addedSectionNo.HasValue)
                        {
                            wizard = new Views.BeamSectionWizardWindow(CurrentInputModel.FoundationBeamInput.Sections);
                            var addedSection = addedSectionNo.Value >= 1
                                ? CurrentInputModel.FoundationBeamInput.Sections.ElementAtOrDefault(addedSectionNo.Value - 1)
                                : null;
                            if (addedSection != null)
                            {
                                wizard.SelectSection(addedSection);
                            }
                        }
                        continueEditing = true;
                    }
                    else
                    {
                        continueEditing = false;
                    }
                }
                else
                {
                    // キャンセルされた
                    continueEditing = false;
                }
            }
        }

        // 材料削除コマンド
        [RelayCommand]
        private void DeleteBeamMaterial(object parameter)
        {
            // DataGridの新規行からの呼び出しを無視
            if (parameter is not BeamMaterial material) return;

            if (CurrentInputModel.FoundationBeamInput?.Materials == null) return;

            // 使用中かチェック (1-based 位置インデックス基準)
            int materialNo = CurrentInputModel.FoundationBeamInput.GetMaterialNo(material);
            if (materialNo < 1) return;
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.MaterialNo == materialNo);
            if (isUsed)
            {
                PileDesign.Services.MessageService.Show(
                    $"材料No.{materialNo}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var before = CaptureInputEdit();
            int deletedNo = materialNo;
            CurrentInputModel.FoundationBeamInput.Materials.Remove(material);
            // 残存材料を再採番し、梁要素の MaterialNo 参照を新しい番号に追従させる
            RenumberMaterialsAndUpdateReferences(deletedNo);
            CompleteInputEdit(before);
        }

        // 断面削除コマンド
        [RelayCommand]
        private void DeleteBeamSection(object parameter)
        {
            // DataGridの新規行からの呼び出しを無視
            if (parameter is not BeamSection section) return;

            if (CurrentInputModel.FoundationBeamInput?.Sections == null) return;

            // 使用中かチェック (1-based 位置インデックス基準)
            int sectionNo = CurrentInputModel.FoundationBeamInput.GetSectionNo(section);
            if (sectionNo < 1) return;
            bool isUsed = CurrentInputModel.FoundationBeamInput.Beams.Any(b => b.SectionNo == sectionNo);
            if (isUsed)
            {
                PileDesign.Services.MessageService.Show(
                    $"断面No.{sectionNo}は梁要素で使用されているため削除できません。",
                    "削除不可",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var before = CaptureInputEdit();
            int deletedNo = sectionNo;
            CurrentInputModel.FoundationBeamInput.Sections.Remove(section);
            // 残存断面を再採番し、梁要素の SectionNo 参照を新しい番号に追従させる
            RenumberSectionsAndUpdateReferences(deletedNo);
            CompleteInputEdit(before);
        }

        // 材料削除後の梁要素 MaterialNo 参照調整。
        // 削除位置 (deletedNo, 1-based) より後ろの材料は位置が 1 つ前にシフトするため、
        // それを参照していた梁要素の MaterialNo を 1 つデクリメントする。
        // No プロパティ廃止に伴い、Material 自身の番号書き換えは不要 (位置 = ID)。
        private void RenumberMaterialsAndUpdateReferences(int deletedNo)
        {
            var fbi = CurrentInputModel?.FoundationBeamInput;
            if (fbi?.Beams == null) return;

            foreach (var beam in fbi.Beams)
            {
                if (beam.MaterialNo > deletedNo)
                    beam.MaterialNo--;
            }
        }

        // 断面削除後の梁要素 SectionNo 参照調整。
        private void RenumberSectionsAndUpdateReferences(int deletedNo)
        {
            var fbi = CurrentInputModel?.FoundationBeamInput;
            if (fbi?.Beams == null) return;

            foreach (var beam in fbi.Beams)
            {
                if (beam.SectionNo > deletedNo)
                    beam.SectionNo--;
            }
        }

        // 一般梁要素プロパティ変更コマンド
        [RelayCommand]
        private void EditBeamElements()
        {
            // 選択された一般梁要素がない場合はメッセージを表示
            var selectedBeams = CurrentInputModel?.FoundationBeamInput?.Beams?.Where(b => b.IsSelected).ToList();
            if (selectedBeams == null || selectedBeams.Count == 0)
            {
                MessageService.Show("一般梁要素が選択されていません。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CurrentInputModel.FoundationBeamInput == null)
            {
                return;
            }

            // ViewModelを作成
            var viewModel = new EditBeamElementViewModel(
                selectedBeams.Count,
                CurrentInputModel.FoundationBeamInput.Materials,
                CurrentInputModel.FoundationBeamInput.Sections);

            // ウィンドウを表示
            var window = new Views.EditBeamElementWindow(viewModel);
            if (window.ShowDialog() == true)
            {
                ApplyBeamElementEdit(selectedBeams, window.Result);
            }
        }
        internal bool ApplyBeamMaterial(int? selectedNo, Views.BeamMaterialWizardWindow.MaterialWizardResult result, out int? addedNo)
        {
            addedNo = null;
            var input = CurrentInputModel.FoundationBeamInput;
            var material = selectedNo.HasValue && selectedNo.Value >= 1
                ? input?.Materials.ElementAtOrDefault(selectedNo.Value - 1) : null;
            if (selectedNo.HasValue && material == null) return false;
            if (material != null && material.Name == result.Name &&
                material.YoungModulus == result.YoungModulus &&
                material.ShearModulus == result.ShearModulus &&
                material.PoissonRatio == result.PoissonRatio) return true;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return false;
            var before = CaptureInputEdit();
            if (input == null) CurrentInputModel.FoundationBeamInput = input = new FoundationBeamInput();
            bool isNew = material == null;
            material ??= new BeamMaterial();
            material.Name = result.Name;
            material.YoungModulus = result.YoungModulus;
            material.ShearModulus = result.ShearModulus;
            material.PoissonRatio = result.PoissonRatio;
            if (isNew)
            {
                input.Materials.Add(material);
                addedNo = input.Materials.Count;
            }
            CompleteInputEdit(before);
            return true;
        }

        internal bool ApplyBeamSection(int? selectedNo, Views.BeamSectionWizardWindow.SectionWizardResult result, out int? addedNo)
        {
            addedNo = null;
            var input = CurrentInputModel.FoundationBeamInput;
            var section = selectedNo.HasValue && selectedNo.Value >= 1
                ? input?.Sections.ElementAtOrDefault(selectedNo.Value - 1) : null;
            if (selectedNo.HasValue && section == null) return false;
            if (section != null && section.Name == result.Name &&
                section.Width == result.Width &&
                section.Height == result.Height &&
                section.Area == result.Area &&
                section.ShearAreaY == result.ShearAreaY &&
                section.ShearAreaZ == result.ShearAreaZ &&
                section.TorsionalMoment == result.TorsionalMoment &&
                section.MomentOfInertiaYY == result.MomentOfInertiaYY &&
                section.MomentOfInertiaZZ == result.MomentOfInertiaZZ &&
                section.AFactor == result.AFactor &&
                section.AyFactor == result.AyFactor &&
                section.AzFactor == result.AzFactor &&
                section.IxxFactor == result.IxxFactor &&
                section.IyyFactor == result.IyyFactor &&
                section.IzzFactor == result.IzzFactor) return true;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return false;
            var before = CaptureInputEdit();
            if (input == null) CurrentInputModel.FoundationBeamInput = input = new FoundationBeamInput();
            bool isNew = section == null;
            section ??= new BeamSection();
            section.Name = result.Name;
            section.Width = result.Width;
            section.Height = result.Height;
            section.Area = result.Area;
            section.ShearAreaY = result.ShearAreaY;
            section.ShearAreaZ = result.ShearAreaZ;
            section.TorsionalMoment = result.TorsionalMoment;
            section.MomentOfInertiaYY = result.MomentOfInertiaYY;
            section.MomentOfInertiaZZ = result.MomentOfInertiaZZ;
            section.AFactor = result.AFactor;
            section.AyFactor = result.AyFactor;
            section.AzFactor = result.AzFactor;
            section.IxxFactor = result.IxxFactor;
            section.IyyFactor = result.IyyFactor;
            section.IzzFactor = result.IzzFactor;
            if (isNew)
            {
                input.Sections.Add(section);
                addedNo = input.Sections.Count;
            }
            CompleteInputEdit(before);
            return true;
        }

        internal bool ApplyBeamElementEdit(IReadOnlyList<FoundationBeam> selectedBeams,
            Views.EditBeamElementWindow.BeamElementEditResult result)
        {
            var input = CurrentInputModel.FoundationBeamInput;
            if (input == null || selectedBeams.Count == 0 || selectedBeams.Any(b => !input.Beams.Contains(b))) return false;
            int? materialNo = result.IsApplicableMaterialNo ? result.MaterialNo : null;
            int? sectionNo = result.IsApplicableSectionNo ? result.SectionNo : null;
            if ((result.IsApplicableMaterialNo && (!materialNo.HasValue || materialNo < 1 || materialNo > input.Materials.Count)) ||
                (result.IsApplicableSectionNo && (!sectionNo.HasValue || sectionNo < 1 || sectionNo > input.Sections.Count))) return false;
            if (!selectedBeams.Any(b => (materialNo.HasValue && b.MaterialNo != materialNo.Value) ||
                (sectionNo.HasValue && b.SectionNo != sectionNo.Value))) return false;
            if (!ConfirmDiscardInvalidatedByInputChange(true)) return false;
            var before = CaptureInputEdit();
            foreach (var beam in selectedBeams)
            {
                if (materialNo.HasValue) beam.MaterialNo = materialNo.Value;
                if (sectionNo.HasValue) beam.SectionNo = sectionNo.Value;
            }
            CompleteInputEdit(before);
            return true;
        }


    }
}

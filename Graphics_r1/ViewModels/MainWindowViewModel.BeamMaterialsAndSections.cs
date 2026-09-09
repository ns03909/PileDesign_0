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
            if (CurrentInputModel.FoundationBeamInput == null)
            {
                CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var wizard = new Views.BeamMaterialWizardWindow(CurrentInputModel.FoundationBeamInput.Materials);

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    int? addedMaterialNo = null;

                    if (wizard.SelectedMaterialNo.HasValue)
                    {
                        // 既存材料の編集 (SelectedMaterialNo は 1-based の位置インデックス)
                        var material = wizard.SelectedMaterialNo.Value >= 1
                            ? CurrentInputModel.FoundationBeamInput.Materials.ElementAtOrDefault(wizard.SelectedMaterialNo.Value - 1)
                            : null;
                        if (material != null)
                        {
                            material.Name = wizard.Result.Name;
                            material.YoungModulus = wizard.Result.YoungModulus;
                            material.ShearModulus = wizard.Result.ShearModulus;
                            material.PoissonRatio = wizard.Result.PoissonRatio;
                        }
                    }
                    else
                    {
                        // 新規材料の追加 (No プロパティは廃止: 位置 = ID)
                        var newMaterial = new BeamMaterial
                        {
                            Name = wizard.Result.Name,
                            YoungModulus = wizard.Result.YoungModulus,
                            ShearModulus = wizard.Result.ShearModulus,
                            PoissonRatio = wizard.Result.PoissonRatio
                        };
                        CurrentInputModel.FoundationBeamInput.Materials.Add(newMaterial);
                        addedMaterialNo = CurrentInputModel.FoundationBeamInput.Materials.Count;
                    }

                    SaveUndoState();
                    RequestUpdateWindow();

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

            if (!CheckAndResetAnalysisResults()) return;

            TrySaveUndoSnapshotSafely();

            foreach (var section in CurrentInputModel.FoundationBeamInput.Sections)
            {
                section.IxxFactor = 0.0;
                // Ixx係数=0 に合わせて TorsionalMoment も再計算
                section.TorsionalMoment = 0.0;
            }

            RequestUpdateWindow();
        }

        // 断面ウィザードコマンド
        [RelayCommand]
        private void OpenSectionWizard()
        {
            if (CurrentInputModel.FoundationBeamInput == null)
            {
                CurrentInputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var wizard = new Views.BeamSectionWizardWindow(CurrentInputModel.FoundationBeamInput.Sections);

            bool continueEditing = true;
            while (continueEditing)
            {
                if (wizard.ShowDialog() == true)
                {
                    int? addedSectionNo = null;

                    if (wizard.SelectedSectionNo.HasValue)
                    {
                        // 既存断面の編集 (SelectedSectionNo は 1-based の位置インデックス)
                        var section = wizard.SelectedSectionNo.Value >= 1
                            ? CurrentInputModel.FoundationBeamInput.Sections.ElementAtOrDefault(wizard.SelectedSectionNo.Value - 1)
                            : null;
                        if (section != null)
                        {
                            section.Name = wizard.Result.Name;
                            section.Width = wizard.Result.Width;
                            section.Height = wizard.Result.Height;
                            section.Area = wizard.Result.Area;
                            section.ShearAreaY = wizard.Result.ShearAreaY;
                            section.ShearAreaZ = wizard.Result.ShearAreaZ;
                            section.TorsionalMoment = wizard.Result.TorsionalMoment;
                            section.MomentOfInertiaYY = wizard.Result.MomentOfInertiaYY;
                            section.MomentOfInertiaZZ = wizard.Result.MomentOfInertiaZZ;
                            section.AFactor = wizard.Result.AFactor;
                            section.AyFactor = wizard.Result.AyFactor;
                            section.AzFactor = wizard.Result.AzFactor;
                            section.IxxFactor = wizard.Result.IxxFactor;
                            section.IyyFactor = wizard.Result.IyyFactor;
                            section.IzzFactor = wizard.Result.IzzFactor;
                        }
                    }
                    else
                    {
                        // 新規断面の追加 (No プロパティは廃止: 位置 = ID)
                        var newSection = new BeamSection
                        {
                            Name = wizard.Result.Name,
                            Width = wizard.Result.Width,
                            Height = wizard.Result.Height,
                            Area = wizard.Result.Area,
                            ShearAreaY = wizard.Result.ShearAreaY,
                            ShearAreaZ = wizard.Result.ShearAreaZ,
                            TorsionalMoment = wizard.Result.TorsionalMoment,
                            MomentOfInertiaYY = wizard.Result.MomentOfInertiaYY,
                            MomentOfInertiaZZ = wizard.Result.MomentOfInertiaZZ,
                            AFactor = wizard.Result.AFactor,
                            AyFactor = wizard.Result.AyFactor,
                            AzFactor = wizard.Result.AzFactor,
                            IxxFactor = wizard.Result.IxxFactor,
                            IyyFactor = wizard.Result.IyyFactor,
                            IzzFactor = wizard.Result.IzzFactor
                        };
                        CurrentInputModel.FoundationBeamInput.Sections.Add(newSection);
                        addedSectionNo = CurrentInputModel.FoundationBeamInput.Sections.Count;
                    }

                    SaveUndoState();
                    RequestUpdateWindow();

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

            int deletedNo = materialNo;
            CurrentInputModel.FoundationBeamInput.Materials.Remove(material);
            // 残存材料を再採番し、梁要素の MaterialNo 参照を新しい番号に追従させる
            RenumberMaterialsAndUpdateReferences(deletedNo);
            SaveUndoState();
            RequestUpdateWindow();
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

            int deletedNo = sectionNo;
            CurrentInputModel.FoundationBeamInput.Sections.Remove(section);
            // 残存断面を再採番し、梁要素の SectionNo 参照を新しい番号に追従させる
            RenumberSectionsAndUpdateReferences(deletedNo);
            SaveUndoState();
            RequestUpdateWindow();
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
            if (!CheckAndResetAnalysisResults()) return;

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
                var result = window.Result;

                // 選択された梁要素のプロパティを一括変更
                foreach (var beam in selectedBeams)
                {
                    if (result.IsApplicableMaterialNo && result.MaterialNo.HasValue)
                    {
                        beam.MaterialNo = result.MaterialNo.Value;
                    }

                    if (result.IsApplicableSectionNo && result.SectionNo.HasValue)
                    {
                        beam.SectionNo = result.SectionNo.Value;
                    }
                }

                SaveUndoState();
                RequestUpdateWindow();
            }
        }
    }
}

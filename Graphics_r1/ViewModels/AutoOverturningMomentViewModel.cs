using CommunityToolkit.Mvvm.ComponentModel;
using PileDesign.Models.InputData;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using PileDesign.Services;

using PileDesign.Common;

namespace PileDesign.ViewModels
{
    public partial class AutoOverturningMomentViewModel : BaseViewModel
    {
        //public InputModel InputModel => InputModel.Instance;
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;
        //public InputModel InputModel => _mainWindowViewModel.CurrentInputModel ?? throw new InvalidOperationException("CurrentInputModel is null. 必ず初期化してください。");


        // SumVL0 / SumVLadd / SumVL は getter で PileLayoutItems から毎回計算するため
        // [ObservableProperty] にせず手書きの get/set を維持する（set は View からの逆方向
        // バインディング許容だが常に再計算される仕様）
        private double _sumVL0;
        public double SumVL0
        {
            get => GetSumVL0();
            set => SetProperty(ref _sumVL0, value);
        }

        private double GetSumVL0()
        {
            double sumVL = 0.0;
            foreach (var item in InputModel.PileLayoutItems)
            {
                sumVL += item.AxialForceVL0;
            }
            return sumVL;
        }

        private double _sumVLadd;
        public double SumVLadd
        {
            get => GetSumVLadd();
            set => SetProperty(ref _sumVLadd, value);
        }

        private double GetSumVLadd()
        {
            double sumVLadd = 0.0;
            foreach (var item in InputModel.PileLayoutItems)
            {
                sumVLadd += item.AxialForceVLAdditional;
            }
            return sumVLadd;
        }

        private double _sumVL;
        public double SumVL
        {
            get => GetSumVL();
            set => SetProperty(ref _sumVL, value);
        }

        private double GetSumVL()
        {
            return GetSumVL0() + GetSumVLadd();
        }

        [ObservableProperty]
        private double _buildingWeight;

        partial void OnBuildingWeightChanged(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _effectiveHeight;

        partial void OnEffectiveHeightChanged(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _shearCoefficient1;

        partial void OnShearCoefficient1Changed(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _overturningMoment1;

        [ObservableProperty]
        private double _shearCoefficient2;

        partial void OnShearCoefficient2Changed(double value) => SetOverturningMoment();

        [ObservableProperty]
        private double _overturningMoment2;

        private void SetOverturningMoment()
        {
            OverturningMoment1 = BuildingWeight * EffectiveHeight * ShearCoefficient1 * 0.001; // MNm
            OverturningMoment2 = BuildingWeight * EffectiveHeight * ShearCoefficient2 * 0.001; // MNm
        }

        [ObservableProperty]
        private bool _isApplicableE1 = true;

        partial void OnIsApplicableE1Changed(bool value) => SetApplicableAllE1();

        private void SetApplicableAllE1()
        {
            for (int i = 0; i < IsApplicableE1s.Count; i++)
            {
                IsApplicableE1s[i] = IsApplicableE1;
            }
        }

        [ObservableProperty]
        private ObservableCollection<bool> _isApplicableE1s = [true, true, true, true];

        [ObservableProperty]
        private bool _isApplicableE2 = true;

        partial void OnIsApplicableE2Changed(bool value) => SetApplicableAllE2();

        private void SetApplicableAllE2()
        {
            for (int i = 0; i < IsApplicableE2s.Count; i++)
            {
                IsApplicableE2s[i] = IsApplicableE2;
            }
        }

        [ObservableProperty]
        private ObservableCollection<bool> _isApplicableE2s = [true, true, true, true];

        [ObservableProperty]
        private bool _isApplicableVL = true;

        // コンストラクタ
        public AutoOverturningMomentViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;
        }

        internal bool TryApplyReactions()
        {
            var changes = new List<(ObservableCollection<double> Loads, int Index, double Value)>();
            try
            {
                if (InputModel.PileLayoutItems.Count == 0) throw new ArgumentException("杭を配置してから反力を適用してください。");
                void Prepare(ObservableCollection<LoadCase> cases, ObservableCollection<bool> selected, double moment, bool level1)
                {
                    if (selected == null || selected.Count < cases.Count) throw new ArgumentException("適用する荷重ケースの指定が不足しています。");
                    for (int i = 0; i < cases.Count; i++)
                    {
                        if (!selected[i]) continue;
                        if (!double.IsFinite(moment) || !double.IsFinite(cases[i].LoadAngle)) throw new ArgumentException("転倒モーメントと荷重方向には有限の数値が必要です。");
                        // 計算できないときは理由 (どの杭の座標か・範囲外か) を示す
                        if (!InputModel.TryGetReactionForUnitMoment(cases[i].LoadAngle, out var reactions, out var problem))
                            throw new ArgumentException("反力を計算できませんでした。" + problem);
                        if (reactions.Count != InputModel.PileLayoutItems.Count || reactions.Any(r => !double.IsFinite(r)))
                            throw new ArgumentException("反力を計算できませんでした。杭配置を確認してください。");
                        if (moment != 0 && reactions.All(r => r == 0)) throw new ArgumentException("指定方向の転倒モーメントを負担できる杭間隔がありません。");
                        for (int j = 0; j < reactions.Count; j++)
                        {
                            var pile = InputModel.PileLayoutItems[j];
                            var loads = level1 ? pile.AxialForceLevel1s : pile.AxialForceLevel2s;
                            if (loads == null || loads.Count <= i) throw new ArgumentException("杭の軸力データが不足しています。");
                            double value = reactions[j] * moment * 1000 + (IsApplicableVL ? pile.AxialForceVL0 : 0);
                            if (!double.IsFinite(value) || !double.IsFinite(value - pile.AxialForceVL))
                                throw new ArgumentException("反力が数値の範囲外です。軸力は変更しません。");
                            if (loads[i] != value) changes.Add((loads, i, value));
                        }
                    }
                }
                Prepare(InputModel.LoadCasesInput.LoadCasesLevel1, IsApplicableE1s, OverturningMoment1, true);
                Prepare(InputModel.LoadCasesInput.LoadCasesLevel2, IsApplicableE2s, OverturningMoment2, false);
            }
            catch (ArgumentException ex) { MessageService.ShowInputRejected(ex); return false; }
            if (changes.Count == 0) return true;
            var before = _mainWindowViewModel.CaptureInputEdit();
            foreach (var change in changes) change.Loads[change.Index] = change.Value;
            _mainWindowViewModel.CompleteInputEdit(before, "転倒モーメントの適用");
            return true;
        }
    }
}

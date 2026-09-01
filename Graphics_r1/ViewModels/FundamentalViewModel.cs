using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using System;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using ToolkitRelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 「基本設定」ウィンドウ。性能グレードと、杭体の材料モデル化オプションを扱う。
    ///
    /// プロジェクト番号・名称・標高記号・Z=0 の標高は
    /// <see cref="ProjectInfoViewModel"/>（プロジェクト情報ウィンドウ）へ分けた。
    /// 保存先はどちらも <see cref="FundamentalInput"/> で同じインスタンスを共有する。
    /// </summary>
    public partial class FundamentalViewModel : ObservableObject, ICloseable
    {
        private readonly UndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;

        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        [ObservableProperty]
        private string _seismicGrade;

        partial void OnSeismicGradeChanged(string value)
        {
            // 性能グレード変更は解析自体（変位・応力）には影響せず、
            // 検定（NM 曲線の選択：損傷限界 / 安全限界）の規則だけが変わるため、
            // 解析結果の削除は行わない（次回の検定で自動的に新しいルールが適用される）。
            var oldValue = InputModel.FundamentalInput.SeismicGrade;

            _undoManager.PushAction(
                () => InputModel.FundamentalInput.SeismicGrade = oldValue,
                () => InputModel.FundamentalInput.SeismicGrade = value,
                "SeismicGrade変更"
            );
            InputModel.FundamentalInput.SeismicGrade = value;
        }

        // バイリニア型コンクリートの引張側降伏応力度を 0 とする
        [ObservableProperty]
        private bool _ignoreConcreteTensileStrength;

        // バイリニア型コンクリートの圧縮側降伏応力度を 0.85·Gsi·Fc とする
        [ObservableProperty]
        private bool _useReducedConcreteCompressiveStrength;

        // 確認ダイアログのキャンセルで値を戻す間、再入を抑制するフラグ
        private bool _suppressConcreteOptionConfirm;

        partial void OnIgnoreConcreteTensileStrengthChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.IgnoreConcreteTensileStrength,
                v => InputModel.FundamentalInput.IgnoreConcreteTensileStrength = v,
                v => IgnoreConcreteTensileStrength = v,
                "コンクリート引張無視の変更");
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        partial void OnUseReducedConcreteCompressiveStrengthChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength,
                v => InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength = v,
                v => UseReducedConcreteCompressiveStrength = v,
                "コンクリート圧縮低減の変更");
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // 鉄筋を 1.1×F で降伏する完全バイリニア型とする
        [ObservableProperty]
        private bool _rebarYieldAt11F;

        partial void OnRebarYieldAt11FChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.RebarYieldAt11F,
                v => InputModel.FundamentalInput.RebarYieldAt11F = v,
                v => RebarYieldAt11F = v,
                "鉄筋1.1F完全バイリニアの変更");
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // 鋼管を 1.1×F で降伏する完全バイリニア型とする
        [ObservableProperty]
        private bool _steelPipeYieldAt11F;

        partial void OnSteelPipeYieldAt11FChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.SteelPipeYieldAt11F,
                v => InputModel.FundamentalInput.SteelPipeYieldAt11F = v,
                v => SteelPipeYieldAt11F = v,
                "鋼管1.1F完全バイリニアの変更");
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // コンクリートのヤング係数 Ec の算定で ξ=1.0 とする
        [ObservableProperty]
        private bool _useUnitGsiForConcreteE;

        partial void OnUseUnitGsiForConcreteEChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseUnitGsiForConcreteE,
                v => InputModel.FundamentalInput.UseUnitGsiForConcreteE = v,
                v => UseUnitGsiForConcreteE = v,
                "コンクリートEc算定のξ=1.0オプション変更");
        }

        // 鋼材のヤング係数に「基礎部材の強度と変形性能」の値を用いる
        // (既定 false = 製品カタログの値。カタログはメーカーで 200,000 / 205,000 に割れる)
        [ObservableProperty]
        private bool _useGuideYoungsModulus;

        partial void OnUseGuideYoungsModulusChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseGuideYoungsModulus,
                v => InputModel.FundamentalInput.UseGuideYoungsModulus = v,
                v => UseGuideYoungsModulus = v,
                "鋼材ヤング係数の出所変更");
        }

        // 場所打ち系コンクリートの許容圧縮を告示1113(第8)による（使用限界=長期・損傷限界=短期）
        [ObservableProperty]
        private bool _useNotification1113Compression;

        partial void OnUseNotification1113CompressionChanged(bool value)
        {
            HandleCapacityOnlyOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseNotification1113Compression,
                v => InputModel.FundamentalInput.UseNotification1113Compression = v,
                v => UseNotification1113Compression = v,
                "許容圧縮を告示1113(第8)による へ変更");
            OnPropertyChanged(nameof(Notification1113CaseEnabled));
            OnPropertyChanged(nameof(UseGuideline2025Appendix13));
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // 場所打ちRC杭のコンクリート許容せん断を告示1113(第8)による（使用限界=長期・損傷限界=短期）
        [ObservableProperty]
        private bool _useNotification1113Shear;

        partial void OnUseNotification1113ShearChanged(bool value)
        {
            HandleCapacityOnlyOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseNotification1113Shear,
                v => InputModel.FundamentalInput.UseNotification1113Shear = v,
                v => UseNotification1113Shear = v,
                "許容せん断を告示1113(第8)による へ変更");
            OnPropertyChanged(nameof(Notification1113CaseEnabled));
            OnPropertyChanged(nameof(UseGuideline2025Appendix13));
        }

        // 告示1113(第8) の区分 ComboBox を有効化するか（圧縮・せん断いずれかON）
        public bool Notification1113CaseEnabled => UseNotification1113Compression || UseNotification1113Shear;

        // 指針安全限界オプションと競合する材料オプション（圧縮 0.85Fc・鋼管 1.1F）を有効化するか。
        // 指針安全限界ON時はグレーアウトして併用を防ぐ（0.85Fc は解析 M-φ と、鋼管 1.1F は指針の
        // 鋼管トリリニアと競合するため）。
        public bool ConflictingMaterialOptionsEnabled => !UseInsituUltimateEFunction;

        // 場所打ちRC杭の安全限界曲げをe関数法で算定（指針(案)5.4.1。検定の耐力側のみ、解析M-φは常にバイリニア）
        [ObservableProperty]
        private bool _useInsituUltimateEFunction;

        partial void OnUseInsituUltimateEFunctionChanged(bool value)
        {
            HandleCapacityOnlyOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseInsituUltimateEFunction,
                v => InputModel.FundamentalInput.UseInsituUltimateEFunction = v,
                v => UseInsituUltimateEFunction = v,
                "安全限界曲げをe関数法(指針準拠) へ変更");
            OnPropertyChanged(nameof(ConflictingMaterialOptionsEnabled));
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        /// <summary>
        /// 2025年版「建築物の構造関係技術基準解説書」付録1-3 の許容耐力に従うマスタースイッチ
        /// （申請実務では必ずチェックを入れる想定）。
        /// 「場所打ち杭の許容圧縮を告示1113(第8)による」「場所打ちRC杭の許容せん断を告示1113(第8)による」
        /// の 2 項目を一括で ON/OFF する。安全限界（終局強度）の算定方式は含まない（個別に選択）。
        /// get は 2 項目とも ON のとき true（個別に切替えると自動で追随）。
        /// </summary>
        public bool UseGuideline2025Appendix13
        {
            get => UseNotification1113Compression && UseNotification1113Shear;
            set
            {
                bool oc = UseNotification1113Compression;
                bool os = UseNotification1113Shear;
                if (oc == value && os == value) return;

                _undoManager.PushAction(
                    () => ApplyGuideline2025(oc, os),
                    () => ApplyGuideline2025(value, value),
                    "2025解説書 付録1-3 許容耐力オプション一括切替");
                ApplyGuideline2025(value, value);
            }
        }

        // 2 項目を一括設定（個別ハンドラを抑制し、キャッシュ破棄＋通知は 1 回にまとめる）
        private void ApplyGuideline2025(bool compression, bool shear)
        {
            bool prev = _suppressConcreteOptionConfirm;
            _suppressConcreteOptionConfirm = true;
            try
            {
                UseNotification1113Compression = compression;   // VM 更新（個別ハンドラは抑制で早期 return）
                UseNotification1113Shear = shear;
                InputModel.FundamentalInput.UseNotification1113Compression = compression;
                InputModel.FundamentalInput.UseNotification1113Shear = shear;
            }
            finally { _suppressConcreteOptionConfirm = prev; }

            _mainWindowViewModel.ApplyConcreteModelOptions();
            OnPropertyChanged(nameof(Notification1113CaseEnabled));
            OnPropertyChanged(nameof(UseGuideline2025Appendix13));
        }

        // 場所打ちRC杭の解析用 M-φ をファイバーモデルで算定する（解析に影響 → 変更時は解析結果リセット）
        [ObservableProperty]
        private bool _useFiberMPhi;

        partial void OnUseFiberMPhiChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseFiberMPhi,
                v => InputModel.FundamentalInput.UseFiberMPhi = v,
                v => UseFiberMPhi = v,
                "M-φをファイバーモデルで算定 へ変更");
        }

        // 告示1113(第8) 長期許容圧縮の区分（1: Fc/4、2: min(Fc/4.5, 6)）
        [ObservableProperty]
        private int _notification1113CompressionCase = 1;

        partial void OnNotification1113CompressionCaseChanged(int value)
        {
            HandleCapacityOnlyCaseChanged(
                value,
                () => InputModel.FundamentalInput.Notification1113CompressionCase,
                v => InputModel.FundamentalInput.Notification1113CompressionCase = v,
                v => Notification1113CompressionCase = v);
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // ─── 場所打ち鋼管コンクリート杭（KCTB / TB 工法）───
        //
        // BCJ評定-FD0356-08 が定めるのは「コンクリートの許容応力度＝告示1113(第8) 打設方法(一)」と
        // 「本体部の設計法＝SRC規準2014 4章2節の累加」。評定書に終局の規定は無いので、
        // εcu = 5,000μ と「許容時の判定に鉄筋を用いない」は評定範囲外の個別オプションとする。

        // 【評定】許容時 N-M を断面分割積分で求める（false = 評定 5.(3) の単純累加）
        [ObservableProperty]
        private bool _useFiberNMForSteelPipeConcrete = true;

        partial void OnUseFiberNMForSteelPipeConcreteChanged(bool value)
        {
            HandleCapacityOnlyOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseFiberNMForSteelPipeConcrete,
                v => InputModel.FundamentalInput.UseFiberNMForSteelPipeConcrete = v,
                v => UseFiberNMForSteelPipeConcrete = v,
                value ? "許容時N-Mを断面分割積分で算定 へ変更" : "許容時N-Mを単純累加で算定 へ変更");
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }

        // 【評定範囲外】終局の圧縮縁ひずみを 5,000μ とする（解析に効く）
        [ObservableProperty]
        private bool _useUltimateStrain5000ForSteelPipeConcrete;

        partial void OnUseUltimateStrain5000ForSteelPipeConcreteChanged(bool value)
        {
            HandleConcreteOptionChanged(
                value,
                () => InputModel.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete,
                v => InputModel.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete = v,
                v => UseUltimateStrain5000ForSteelPipeConcrete = v,
                "終局の圧縮縁ひずみを 5,000μ とする へ変更");
        }

        // 【評定範囲外】許容時の判定に鉄筋を用いない（耐力側のみ）
        [ObservableProperty]
        private bool _excludeRebarFromAllowableLimitForSteelPipeConcrete;

        partial void OnExcludeRebarFromAllowableLimitForSteelPipeConcreteChanged(bool value)
        {
            HandleCapacityOnlyOptionChanged(
                value,
                () => InputModel.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete,
                v => InputModel.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete = v,
                v => ExcludeRebarFromAllowableLimitForSteelPipeConcrete = v,
                "許容時の判定に鉄筋を用いない へ変更");
        }

        /// <summary>
        /// BCJ評定-FD0356-08 が定める項目に従うマスタースイッチ。
        ///
        /// 評定が定めているのは次の 2 つだけで、終局ひずみや許容時の判定は評定範囲外のため含めない。
        ///   ・コンクリートの許容応力度 = 告示1113(第8) 打設方法(一)（評定 5.(1)・表1.2）
        ///   ・本体部の設計法 = 単純累加（評定 5.(3)、SRC規準2014 4章2節）
        /// get は構成項目が評定どおりのとき true（個別に切替えると自動で追随）。
        /// ON のとき適用範囲（φ700〜2700・板厚下限・鋼管長・腐食しろ 1mm・Fc 18〜45）の検査も働く。
        /// </summary>
        public bool FollowsKctbEvaluation
        {
            get => UseNotification1113Compression
                   && Notification1113CompressionCase == 1
                   && !UseFiberNMForSteelPipeConcrete;
            set
            {
                if (FollowsKctbEvaluation == value) return;

                var before = CaptureKctbState();
                var after = value
                    ? new KctbState(true, 1, false)
                    : new KctbState(before.Notification1113, before.Case, true);

                _undoManager.PushAction(
                    () => ApplyKctb(before),
                    () => ApplyKctb(after),
                    "BCJ評定-FD0356-08 準拠の一括切替");
                ApplyKctb(after);
            }
        }

        // 評定マスターが束ねる構成項目（いずれも検定の耐力側のみ。解析結果は保持される）
        private readonly record struct KctbState(bool Notification1113, int Case, bool FiberNM);

        private KctbState CaptureKctbState() =>
            new(UseNotification1113Compression, Notification1113CompressionCase, UseFiberNMForSteelPipeConcrete);

        // 構成項目を一括設定（個別ハンドラを抑制し、キャッシュ破棄と通知を 1 回にまとめる）
        private void ApplyKctb(KctbState s)
        {
            if (CaptureKctbState().Equals(s)) return;

            bool prev = _suppressConcreteOptionConfirm;
            _suppressConcreteOptionConfirm = true;
            try
            {
                UseNotification1113Compression = s.Notification1113;
                Notification1113CompressionCase = s.Case;
                UseFiberNMForSteelPipeConcrete = s.FiberNM;

                var f = InputModel.FundamentalInput;
                f.UseNotification1113Compression = s.Notification1113;
                f.Notification1113CompressionCase = s.Case;
                f.UseFiberNMForSteelPipeConcrete = s.FiberNM;
            }
            finally { _suppressConcreteOptionConfirm = prev; }

            _mainWindowViewModel.ApplyConcreteModelOptions();
            OnPropertyChanged(nameof(Notification1113CaseEnabled));
            OnPropertyChanged(nameof(UseGuideline2025Appendix13));
            OnPropertyChanged(nameof(FollowsKctbEvaluation));
        }


        // 告示1113(第8) 圧縮の区分 ComboBox 用（値=区分番号、表示=式）
        public sealed record Notification1113CaseItem(int Value, string Display);
        public Notification1113CaseItem[] Notification1113CompressionCaseOptions { get; } =
        [
            new Notification1113CaseItem(1, "[1] 圧縮 Fc/4・せん断 Fc/40"),
            new Notification1113CaseItem(2, "[2] 圧縮 min(Fc/4.5, 6)・せん断 Fc/45"),
        ];

        /// <summary>
        /// バイリニアコンクリート・オプションの変更を処理する共通ハンドラ。
        /// これらは M-φ（→ 非線形 FEM 解析）に影響するため、解析結果があれば確認のうえリセットする
        /// （杭要素分割＝メッシュは材料変更では不変のため保持）。確認キャンセル時はチェックを元に戻す。
        /// </summary>
        private void HandleConcreteOptionChanged(
            bool value, Func<bool> getter, Action<bool> setModel, Action<bool> setVm, string reason)
        {
            if (_suppressConcreteOptionConfirm) return;

            bool oldValue = getter();
            if (oldValue == value) return;

            if (!_mainWindowViewModel.CheckAndResetAnalysisResultsKeepingSplit(reason))
            {
                _suppressConcreteOptionConfirm = true;
                try { setVm(oldValue); }
                finally { _suppressConcreteOptionConfirm = false; }
                return;
            }

            _undoManager.PushAction(
                () => { setModel(oldValue); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                () => { setModel(value); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                reason);

            setModel(value);
            _mainWindowViewModel.ApplyConcreteModelOptions();
        }

        /// <summary>
        /// 検定の耐力側（使用/損傷限界 NM）のみに効くオプション用ハンドラ。
        /// 解析（M-φ・変形・応力）・安全限界には影響しないため、解析結果は削除せず保持する
        /// （確認ダイアログ無し）。キャッシュ破棄は ApplyConcreteModelOptions が行う。
        /// </summary>
        private void HandleCapacityOnlyOptionChanged(
            bool value, Func<bool> getter, Action<bool> setModel, Action<bool> setVm, string reason)
        {
            if (_suppressConcreteOptionConfirm) return;
            bool oldValue = getter();
            if (oldValue == value) return;

            _undoManager.PushAction(
                () => { setModel(oldValue); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                () => { setModel(value); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                reason);

            setModel(value);
            _mainWindowViewModel.ApplyConcreteModelOptions();
        }

        // 区分(int)用の capacity-only ハンドラ（解析結果は保持）
        private void HandleCapacityOnlyCaseChanged(
            int value, Func<int> getter, Action<int> setModel, Action<int> setVm)
        {
            if (_suppressConcreteOptionConfirm) return;
            int oldValue = getter();
            if (oldValue == value) return;

            _undoManager.PushAction(
                () => { setModel(oldValue); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                () => { setModel(value); _mainWindowViewModel.ApplyConcreteModelOptions(); },
                "告示1113(第8) 圧縮区分の変更");

            setModel(value);
            _mainWindowViewModel.ApplyConcreteModelOptions();
        }

        [ObservableProperty]
        private Point3D _point3D0;

        // Undo/Redoコマンド
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }

        public IRelayCommand OkCommand { get; }
        public IRelayCommand CancelCommand { get; }

        public ICommand CloseWindowCommand { get; }

        // RequestCloseイベントの実装
        public event EventHandler RequestClose;

        // 
        private FundamentalInput PrevFundamentalInput { get; set; }

        public string[] SeismicGradeOptions { get; } = ["S", "A"];

        // コンストラクタ
        public FundamentalViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel;

            // ShallowCopyメソッドを使用して値渡し
            PrevFundamentalInput = InputModel.FundamentalInput.ShallowCopy();

            Point3D0 = InputModel.FundamentalInput.Point3D0;
            SeismicGrade = InputModel.FundamentalInput.SeismicGrade;
            IgnoreConcreteTensileStrength = InputModel.FundamentalInput.IgnoreConcreteTensileStrength;
            UseReducedConcreteCompressiveStrength = InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength;
            RebarYieldAt11F = InputModel.FundamentalInput.RebarYieldAt11F;
            SteelPipeYieldAt11F = InputModel.FundamentalInput.SteelPipeYieldAt11F;
            UseUnitGsiForConcreteE = InputModel.FundamentalInput.UseUnitGsiForConcreteE;
            UseGuideYoungsModulus = InputModel.FundamentalInput.UseGuideYoungsModulus;
            UseNotification1113Compression = InputModel.FundamentalInput.UseNotification1113Compression;
            UseNotification1113Shear = InputModel.FundamentalInput.UseNotification1113Shear;
            UseInsituUltimateEFunction = InputModel.FundamentalInput.UseInsituUltimateEFunction;
            UseFiberMPhi = InputModel.FundamentalInput.UseFiberMPhi;
            UseUltimateStrain5000ForSteelPipeConcrete = InputModel.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete;
            ExcludeRebarFromAllowableLimitForSteelPipeConcrete = InputModel.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete;
            UseFiberNMForSteelPipeConcrete = InputModel.FundamentalInput.UseFiberNMForSteelPipeConcrete;
            Notification1113CompressionCase = InputModel.FundamentalInput.Notification1113CompressionCase;

            InputModel.FundamentalInput.PropertyChanged += FundamentalInput_PropertyChanged;


            // ToolkitRelayCommand (パラメータ無し Action / Func<bool> 対応)
            UndoCommand = new ToolkitRelayCommand(Undo, () => _undoManager.CanUndo);
            RedoCommand = new ToolkitRelayCommand(Redo, () => _undoManager.CanRedo);
            OkCommand = new ToolkitRelayCommand(OnOk);
            CancelCommand = new ToolkitRelayCommand(OnCancel);
            CloseWindowCommand = new ToolkitRelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        }

        private void Undo() => _undoManager.Undo();
        private void Redo() => _undoManager.Redo();


        private void OnOk()
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancel()
        {
            // プロパティを元に戻す処理
            InputModel.FundamentalInput = PrevFundamentalInput.ShallowCopy();
            // 復元した基本設定の値で静的オプションを再同期（解析結果は既に確認のうえ削除済み）
            _mainWindowViewModel.ApplyConcreteModelOptions();
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void FundamentalInput_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(FundamentalInput.SeismicGrade):
                    SeismicGrade = InputModel.FundamentalInput.SeismicGrade;
                    break;
                case nameof(FundamentalInput.IgnoreConcreteTensileStrength):
                    IgnoreConcreteTensileStrength = InputModel.FundamentalInput.IgnoreConcreteTensileStrength;
                    break;
                case nameof(FundamentalInput.UseReducedConcreteCompressiveStrength):
                    UseReducedConcreteCompressiveStrength = InputModel.FundamentalInput.UseReducedConcreteCompressiveStrength;
                    break;
                case nameof(FundamentalInput.RebarYieldAt11F):
                    RebarYieldAt11F = InputModel.FundamentalInput.RebarYieldAt11F;
                    break;
                case nameof(FundamentalInput.SteelPipeYieldAt11F):
                    SteelPipeYieldAt11F = InputModel.FundamentalInput.SteelPipeYieldAt11F;
                    break;
                case nameof(FundamentalInput.UseUnitGsiForConcreteE):
                    UseUnitGsiForConcreteE = InputModel.FundamentalInput.UseUnitGsiForConcreteE;
                    break;
                case nameof(FundamentalInput.UseGuideYoungsModulus):
                    UseGuideYoungsModulus = InputModel.FundamentalInput.UseGuideYoungsModulus;
                    break;
                case nameof(FundamentalInput.UseNotification1113Compression):
                    UseNotification1113Compression = InputModel.FundamentalInput.UseNotification1113Compression;
                    break;
                case nameof(FundamentalInput.UseNotification1113Shear):
                    UseNotification1113Shear = InputModel.FundamentalInput.UseNotification1113Shear;
                    break;
                case nameof(FundamentalInput.UseInsituUltimateEFunction):
                    UseInsituUltimateEFunction = InputModel.FundamentalInput.UseInsituUltimateEFunction;
                    break;
                case nameof(FundamentalInput.UseFiberMPhi):
                    UseFiberMPhi = InputModel.FundamentalInput.UseFiberMPhi;
                    break;
                case nameof(FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete):
                    UseUltimateStrain5000ForSteelPipeConcrete = InputModel.FundamentalInput.UseUltimateStrain5000ForSteelPipeConcrete;
                    break;
                case nameof(FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete):
                    ExcludeRebarFromAllowableLimitForSteelPipeConcrete = InputModel.FundamentalInput.ExcludeRebarFromAllowableLimitForSteelPipeConcrete;
                    break;
                case nameof(FundamentalInput.UseFiberNMForSteelPipeConcrete):
                    UseFiberNMForSteelPipeConcrete = InputModel.FundamentalInput.UseFiberNMForSteelPipeConcrete;
                    break;
                case nameof(FundamentalInput.Notification1113CompressionCase):
                    Notification1113CompressionCase = InputModel.FundamentalInput.Notification1113CompressionCase;
                    break;
            }
        }
    }
}
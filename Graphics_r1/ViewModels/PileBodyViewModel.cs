using PileDesign.Constants;
//using System.Windows.Forms.DataVisualization.Charting;
//using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Common.Undo;
using PileDesign.Models.InputData;
using PileDesign.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PileDesign.Services;

namespace PileDesign.ViewModels
{
    /// <summary>
    ///    PileBodyViewModelクラス
    /// </summary>
    public partial class PileBodyViewModel : ObservableObject, ICloseable
    {

        public readonly UndoManager _undoManager = new();

        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;
        // 杭体
        [ObservableProperty]
        private ObservableCollection<PileBodyInput> _pileBodies;

        // PileBody は外部からの書き込みを防ぐため private setter を維持
        private PileBodyInput _pileBody;
        public PileBodyInput PileBody
        {
            get => _pileBody;
            private set => SetProperty(ref _pileBody, value);
        }

        // 杭体数+1リスト
        //private ObservableCollection<int> _pileBodiesCountPlusOneList;
        //public ObservableCollection<int> PileBodiesCountPlusOneList
        //{
        //    get => _pileBodiesCountPlusOneList;
        //    private set => SetProperty(ref _pileBodiesCountPlusOneList, value);
        //}

        [ObservableProperty]
        private ObservableCollection<string> _pileBodiesCountPlusOneList;


        //private void UpdatePileBodiesCountPlusOneList()
        //{
        //    var countPlusOneList = new ObservableCollection<int>(Enumerable.Range(1, PileBodies.Count + 1));
        //    PileBodiesCountPlusOneList = countPlusOneList;
        //}
        private void UpdatePileBodiesCountPlusOneList()
        {
            var list = new ObservableCollection<string>();
            int count = PileBodies.Count;
            for (int i = 1; i <= count; i++)
            {
                list.Add(i.ToString());
            }
            list.Add($"{count + 1} (New)");
            PileBodiesCountPlusOneList = list;
        }

        // 杭体番号
        private int _pileBodyNo = 1;
        public int PileBodyNo
        {
            get => _pileBodyNo;
            set
            {
                if (value <= 0) return; // 0以下は無視
                if (SetProperty(ref _pileBodyNo, value))
                {
                    UpdateTemporarySoilPile();
                }
            }
        }

        // 杭頭レベル
        [ObservableProperty]
        private double _pileTopAltitude;

        partial void OnPileTopAltitudeChanged(double value)
        {
            DrawShapes();
            UpdateTemporarySoilPile();
        }

        // 地盤番号 — setter は値変化の有無に関わらず常に副作用を走らせる既存仕様のため
        // partial 移行不可（OnXxxChanged は値変化時のみ呼ばれる）。手書き維持。
        private int _selectedGroundNo = 1;
        public int SelectedGroundNo
        {
            get => _selectedGroundNo;
            set
            {
                SetProperty(ref _selectedGroundNo, value);
                SetGroundInput();
                UpdateTemporarySoilPile();
            }
        }

        // 地層描画
        [ObservableProperty]
        private bool _isGroundLayerOverwrapped;

        partial void OnIsGroundLayerOverwrappedChanged(bool value)
        {
            SetGroundInput();
            UpdateTemporarySoilPile();
        }

        // 液状化FL 杭姿図への重ね描画
        [ObservableProperty]
        private bool _isLiquefactionFLVisible;

        partial void OnIsLiquefactionFLVisibleChanged(bool value) => DrawShapes();

        // 地盤変位 杭姿図への重ね描画
        [ObservableProperty]
        private bool _isGroundDisplacementVisible;

        partial void OnIsGroundDisplacementVisibleChanged(bool value) => DrawShapes();

        // 地震レベル (0=L1, 1=L2) — FL / 地盤変位の対象レベル
        [ObservableProperty]
        private int _selectedSeismicLevelIndex;

        partial void OnSelectedSeismicLevelIndexChanged(int value) => DrawShapes();

        // 地盤変位 液状化考慮 (true=DmaxUStarSigmaGammaCyH、false=DmaxUStar)
        [ObservableProperty]
        private bool _isDisplacementWithLiquefaction;

        partial void OnIsDisplacementWithLiquefactionChanged(bool value) => DrawShapes();

        // 選択中地盤
        [ObservableProperty]
        private GroundInput _selectedGroundInput;

        private void SetGroundInput()
        {
            // ガード: SelectedGroundNo が範囲外 (例: GroundsInput 1 件しかない計算例で初期値 ≥ 2 になっているケース等) の場合は
            // 範囲内にクランプして例外を防ぐ
            if (InputModel?.GroundsInput == null || InputModel.GroundsInput.Count == 0)
            {
                SelectedGroundInput = null;
                return;
            }
            int idx = SelectedGroundNo - 1;
            if (idx < 0) idx = 0;
            if (idx >= InputModel.GroundsInput.Count) idx = InputModel.GroundsInput.Count - 1;
            SelectedGroundInput = InputModel.GroundsInput[idx];
        }

        private string _selectedPileBodyNoItem;
        public string SelectedPileBodyNoItem
        {
            get => _selectedPileBodyNoItem;
            set
            {
                if (SetProperty(ref _selectedPileBodyNoItem, value))
                {
                    if (value != null && value.Contains("New"))
                    {
                        int newNo = PileBodies.Count + 1;
                        PileBodies.Add(new PileBodyInput() { PileBodyRef = "(PB" + newNo.ToString() + ")" });
                        UpdatePileBodiesCountPlusOneList();

                        // 再割り当てを避けるためにローカル変数を使用
                        var newItem = PileBodiesCountPlusOneList[PileBodies.Count - 1];
                        _selectedPileBodyNoItem = newItem; // 内部フィールドを直接更新
                        OnPropertyChanged(nameof(SelectedPileBodyNoItem));

                        PileBodyNo = PileBodies.Count;
                        PileBody = PileBodies.Last();
                    }
                    else
                    {
                        int idx = PileBodiesCountPlusOneList.IndexOf(value);
                        if (idx >= 0 && idx < PileBodies.Count)
                        {
                            PileBodyNo = idx + 1;
                            PileBody = PileBodies[idx];
                            // 杭体タイプに応じて杭頭タイプオプションを再設定
                            SyncPileTopTypeOption();
                        }
                    }
                    DrawShapes();
                    UpdateTemporarySoilPile();
                }
            }
        }

        // 仮のSoilPile
        [ObservableProperty]
        private SoilPile _temporarySoilPile;

        // xamlフィールド
        public Canvas Canvas { get; set; }

        public ComboBox ComboBoxPileBodyNo { get; set; }
        public TextBox TextBoxPileBodyRef { get; set; }

        public TextBox TextBoxPileToeDia { get; set; }
        public TextBox TextBoxPrecastPileTipNonPermeability { get; set; }
        public TextBox TextBoxSteelPileTipNonPermeability { get; set; }
        public TextBox TextBoxSettleAlpha { get; set; }
        public TextBox TextBoxSettleN { get; set; }

        public ComboBox ComboBoxPileTopType { get; set; }
        public ComboBox ComboBoxPresetSettlementParameters { get; set; }

        public ObservableCollection<PileBodySegment> SelectedPileSegments { get; set; }

        // Viewを閉じるためのイベント
        public event EventHandler RequestClose;
        public event EventHandler RecalculateDataGridPileBodyCompleted;

        //// RequestCloseイベントの実装
        protected virtual void OnRecalculateDataGridPileBodyCompleted(EventArgs e)
        {
            RecalculateDataGridPileBodyCompleted?.Invoke(this, e);
        }

        private readonly ObservableCollection<PileBodyInput> PrevPileBodies;

        // コンストラクタ
        public PileBodyViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            // 各 PileBodyInput の深いコピーを作成して新しい ObservableCollection に追加
            PrevPileBodies = new ObservableCollection<PileBodyInput>(
                InputModel.PileBodies.Select(pileBody => pileBody.DeepCopy())
            );

            PileBodies = new ObservableCollection<PileBodyInput>(
                InputModel.PileBodies.Select(pileBody => pileBody.DeepCopy())
                );
            // 開いたときの杭体番号を控える。杭配置の杭体番号は、OK で閉じたときにこれで付け直す
            for (int i = 0; i < PileBodies.Count; i++)
                if (PileBodies[i] != null) PileBodies[i].NoAtEditStart = i + 1;

            UpdatePileBodiesCountPlusOneList();

            PileBody = CurrentBody;


            // 初期選択を設定。入力の問題から開いたときは、その杭体を選んでおく
            if (PileBodiesCountPlusOneList != null && PileBodiesCountPlusOneList.Count > 0)
            {
                int focus = mainWindowViewModel.InputFocus?.PileBodyNo ?? 0;
                SelectedPileBodyNoItem = PileBodiesCountPlusOneList[focus >= 1 && focus <= PileBodies.Count ? focus - 1 : 0];
            }


            // xaml
            ComboBoxPileBodyNo = new();
            TextBoxPileBodyRef = new();
            TextBoxPileToeDia = new();
            TextBoxPrecastPileTipNonPermeability = new();
            TextBoxSteelPileTipNonPermeability = new();
            TextBoxSettleAlpha = new();
            TextBoxSettleN = new();

            SetPileTopTypeAndConstruction();


            // 例: PileBodyViewModel コンストラクタ末尾など
            foreach (var s in PileBodyInput.PileBodyTypeOption) //Serilog.Log.Debug("  >" + s);

            // デバッグ：InputModel と ViewModel の PileBodyType を確認
            if (InputModel?.PileBodies != null)
            {
                for (int i = 0; i < InputModel.PileBodies.Count; i++)
                {
                }
            }

            if (PileBodies != null)
            {
                for (int i = 0; i < PileBodies.Count; i++)
                {
                }

                // 安全措置：もし null/空 の要素があれば既定値で埋める（UIが空になるのを防ぐ）
                var defaultType = PileBodyInput.PileBodyTypeOption != null && PileBodyInput.PileBodyTypeOption.Count > 0
                    ? PileBodyInput.PileBodyTypeOption[0]
                    : PileTypeNames.InsituRc;

                foreach (var pb in PileBodies)
                {
                    if (string.IsNullOrWhiteSpace(pb.PileBodyType))
                    {
                        pb.PileBodyType = defaultType;
                    }
                }
            }
        }

        /// <summary>
        /// 選択中の杭体 (画面が持つ一覧を、選択中の杭体番号で引く)。番号を一覧の範囲に丸めて引くので、範囲の外で落ちない。
        /// 番号は削除・元に戻すのたびに範囲へ戻している (<see cref="KeepSelectionInRange"/>) ので、丸めが効くのは想定外の状態だけ。
        /// 杭体は最後の 1 つを消せないので、一覧は空にならない。
        /// </summary>
        internal PileBodyInput CurrentBody => PileBodies[Math.Clamp(PileBodyNo, 1, PileBodies.Count) - 1];

        /// <summary>番号 (1 から) の杭体。範囲の外なら null。</summary>
        private PileBodyInput? BodyAt(int no) => no >= 1 && no <= PileBodies.Count ? PileBodies[no - 1] : null;

        /// <summary>選択中の杭体番号を、いまの杭体の数の範囲に戻し、選択中の杭体を合わせる。</summary>
        private void KeepSelectionInRange()
        {
            UpdatePileBodiesCountPlusOneList();
            if (PileBodies.Count == 0) { PileBody = null; return; }
            if (PileBodyNo > PileBodies.Count) PileBodyNo = PileBodies.Count;
            PileBody = CurrentBody;
        }

        [RelayCommand]
        public void Undo()
        {
            // Redo時に現在のライブ状態を復元できるよう、Undo前に履歴へ追加
            if (_undoManager.CurrentIndex == _undoManager.History.Count - 1)
            {
                _undoManager.SaveState(new ObservableCollection<PileBodyInput>(PileBodies.Select(pb => pb.DeepCopy())));
            }
            _undoManager.UndoSnapshot();
            if (_undoManager.CurrentState is ObservableCollection<PileBodyInput> state)
            {
                // 深いコピーで反映
                PileBodies = new ObservableCollection<PileBodyInput>(state.Select(pb => pb.DeepCopy()));
                // 選択状態も復元。杭体の数が変わっていれば、選択を範囲内に戻す (範囲外のままだと次の操作で落ちる)
                KeepSelectionInRange();
                DrawShapes();
                UpdateTemporarySoilPile();
            }
        }

        [RelayCommand]
        public void Redo()
        {
            _undoManager.RedoSnapshot();
            if (_undoManager.CurrentState is ObservableCollection<PileBodyInput> state)
            {
                PileBodies = new ObservableCollection<PileBodyInput>(state.Select(pb => pb.DeepCopy()));
                KeepSelectionInRange();
                DrawShapes();
                UpdateTemporarySoilPile();
            }
        }


        [RelayCommand]
        public void DeletePileBody()
        {
            // 杭体が1つしかない場合は削除不可
            if (PileBodies.Count <= 1)
            {
                MessageService.Show("杭体が1つしか存在しないため、削除できません。", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 選択中の杭体番号
            int index = PileBodyNo - 1;
            if (index < 0 || index >= PileBodies.Count)
            {
                MessageService.Show("削除対象が選択されていません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 杭配置からの参照チェック (使用中なら削除を拒否)。
            // 杭配置はまだ「開いたときの番号」で杭体を指している (付け直すのは OK で閉じたとき) ので、
            // 画面の番号ではなく開いたときの番号で照合する。画面で足した杭体 (0) を指す杭は無い
            int originNo = PileBodies[index]?.NoAtEditStart ?? 0;
            var referencingPiles = _mainWindowViewModel?.CurrentInputModel?.PileLayoutItems?
                .Where(p => p != null && originNo > 0 && p.PileBodyNo == originNo)
                .Select(p => p.PileNo)
                .OrderBy(no => no)
                .ToList();
            if (referencingPiles != null && referencingPiles.Count > 0)
            {
                string list = string.Join(", ", referencingPiles.Take(20).Select(n => $"#{n}"));
                if (referencingPiles.Count > 20) list += $" ほか {referencingPiles.Count - 20} 件";
                MessageService.Show(
                    $"杭体番号 {PileBodyNo} は杭配置 {list} が参照中のため削除できません。\n" +
                    $"先に杭配置側で杭体番号を別の値に変更してから削除してください。",
                    "削除不可",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // より大きい杭体番号を参照している PileLayoutItem は、削除後にインデックスが
            // 1 つずれて意味が変わる (旧 #3 → 新 #2)。アラートで通知し、自動リナンバリングする
            // か削除中止かをユーザーに選択させる。
            var shiftingPiles = _mainWindowViewModel?.CurrentInputModel?.PileLayoutItems?
                .Where(p => p != null && originNo > 0 && p.PileBodyNo > originNo)
                .Select(p => p.PileNo)
                .OrderBy(no => no)
                .ToList();
            if (shiftingPiles != null && shiftingPiles.Count > 0)
            {
                string list = string.Join(", ", shiftingPiles.Take(20).Select(n => $"#{n}"));
                if (shiftingPiles.Count > 20) list += $" ほか {shiftingPiles.Count - 20} 件";
                var shiftResult = MessageService.Show(
                    $"杭体番号 {PileBodyNo} を削除すると、より大きい杭体番号を参照している杭配置 {list} の番号が 1 つずれます。\n\n" +
                    $"・OK: 該当する杭配置の杭体番号を自動的に 1 つ下げて削除します (このウィンドウを OK で閉じたときに付け直します)\n" +
                    $"・キャンセル: 削除を中止します",
                    "番号シフトの確認",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (shiftResult != MessageBoxResult.OK) return;
            }

            // 確認メッセージ
            var result = MessageService.Show(
                $"杭体番号 {PileBodyNo} を削除しますか？\n元に戻せません。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // 杭配置の杭体番号はここでは書き換えない。以前はここで下げていたので、このあと
                // キャンセル・× で閉じる、または「元に戻す」で杭体を戻すと、杭体の一覧は元のままなのに
                // 杭配置だけ番号が下がり、杭が<b>黙って別の杭体を指した</b>。OK で閉じたときに
                // 開いたときの番号 (NoAtEditStart) から付け直す (RenumberPileLayout)。
                PileBodies.RemoveAt(index);
                UpdatePileBodiesCountPlusOneList();

                // 削除後の選択状態を調整
                if (PileBodies.Count > 0)
                {
                    PileBodyNo = Math.Min(PileBodyNo, PileBodies.Count);
                    PileBody = CurrentBody;
                }
                else
                {
                    PileBodyNo = 1;
                    PileBody = null;
                }

                DrawShapes();
                UpdateTemporarySoilPile();
            }
        }

        public void UpdateTemporarySoilPile()
        {
            if (PileBody == null || SelectedGroundInput == null)
                return; // または適切なエラー処理

            var pileBodyCopy = PileBody.DeepCopy();
            var groundCopy = SelectedGroundInput.DeepCopy();

            if (pileBodyCopy == null || groundCopy == null)
                return; // または適切なエラー処理
            // ZDataItemsの生成
            var zDataItems = new ObservableCollection<PileZDataItem>();
            double z = PileTopAltitude;
            zDataItems.Add(new PileZDataItem
            {
                Z = z,
                GroundInput = SelectedGroundInput
            });
            foreach (var seg in pileBodyCopy.PileBodySegments)
            {
                z -= seg.SegmentLength;
                zDataItems.Add(new PileZDataItem
                {
                    Z = z,
                    GroundInput = SelectedGroundInput
                });
            }

            int temporalyPileBodyNo = 1;

            //TemporarySoilPile = new SoilPile(
            //    SelectedGroundNo,
            //    InputModel.GroundsInput[SelectedGroundNo - 1],
            //    temporalyPileBodyNo,
            //    pileBodyCopy,
            //    pileTopAltitude: PileTopAltitude,
            //    zDataItems: zDataItems
            //);
            // ガード: SelectedGroundNo が範囲外でも例外を起こさない
            int sgIdx = SelectedGroundNo - 1;
            if (InputModel?.GroundsInput == null || InputModel.GroundsInput.Count == 0)
                return;
            if (sgIdx < 0) sgIdx = 0;
            if (sgIdx >= InputModel.GroundsInput.Count) sgIdx = InputModel.GroundsInput.Count - 1;

            TemporarySoilPile = new SoilPile()
            {
                GroundNo = sgIdx + 1,
                GroundInput = InputModel.GroundsInput[sgIdx],
                PileBodyNo = temporalyPileBodyNo,
                PileBodyInput = pileBodyCopy,
                Z = zDataItems[0].Z,
                ZDataItems = zDataItems
            };

            TemporarySoilPile.UpdateProperties(); // ←これを必ず呼ぶ
            TemporarySoilPile.NotifyAllPropertiesChanged();
        }

        [RelayCommand]
        private void OnOk()
        {
            // 追加: 編集前に杭要素分割解除確認
            if (!_mainWindowViewModel.CheckAndResetElementSplit("杭体"))
                return; // キャンセル時は処理中断

            // 場所打ち鋼管コンクリート杭の最上段区間が「鉄筋コンクリート部」になっていないかチェック
            // (通常、最上段は「鋼管コンクリート部」を選択する)
            var insituSteelPipeIssues = CheckInsituSteelPipeTopSection(PileBodies);
            if (insituSteelPipeIssues.Count > 0)
            {
                string list = string.Join(", ", insituSteelPipeIssues);
                var result = MessageService.Show(
                    $"以下の杭体で、最上段区間が「鉄筋コンクリート部」になっています:\n{list}\n\n" +
                    "場所打ち鋼管コンクリート杭の最上段は通常「鋼管コンクリート部」を選択します。\n" +
                    "このまま保存しますか？",
                    "杭断面タイプの確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes) return;
            }

            // 杭配置の杭体番号を、画面で消した・並びが変わった杭体に合わせて付け直す
            var unresolved = RenumberPileLayout(_mainWindowViewModel?.CurrentInputModel?.PileLayoutItems, PileBodies);
            foreach (var u in unresolved) Serilog.Log.Warning("[杭体] 杭配置の杭体番号を付け直せません: {Pile}", u);
            InputModel.PileBodies = PileBodies;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 杭配置の杭体番号を、編集後の杭体の並びに合わせて付け直す。杭配置は開いたときの番号で杭体を指しているので、
        /// その番号を持つ杭体 (<see cref="PileBodyInput.NoAtEditStart"/>) のいまの位置へ移す。
        /// 付け直せなかった杭 (指す杭体を消した) の説明を返す (杭体の削除は参照中なら断るので、通常は空)。
        /// </summary>
        internal static List<string> RenumberPileLayout(IEnumerable<PileLayoutDataItem>? piles, IList<PileBodyInput> editedBodies)
            => PileDesign.Services.EditRenumbering.Apply(
                (piles ?? []).Where(p => p != null).Select(p => new PileDesign.Services.EditRenumbering.Reference(
                    $"杭 No.{p.No}", () => p.PileBodyNo, n => p.PileBodyNo = n)),
                PileDesign.Services.EditRenumbering.NewNumberByOrigin(editedBodies.Select(b => b?.NoAtEditStart ?? 0)),
                "杭体");

        /// <summary>
        /// 場所打ち鋼管コンクリート杭の最上段区間が「鉄筋コンクリート部」になっている杭体を抽出する。
        /// 戻り値: 該当する杭体の表示名リスト (例: ["杭体 1", "杭体 3"])。
        /// </summary>
        internal static List<string> CheckInsituSteelPipeTopSection(IEnumerable<PileDesign.Models.InputData.PileBodyInput> pileBodies)
        {
            var issues = new List<string>();
            if (pileBodies == null) return issues;
            int no = 0;
            foreach (var pb in pileBodies)
            {
                no++;
                if (pb?.PileBodyType != PileTypeNames.InsituSteelPipeConcrete) continue;
                if (pb.PileBodySegments == null || pb.PileBodySegments.Count == 0) continue;
                var topSection = pb.PileBodySegments[0]?.PileSection;
                if (topSection == null) continue;
                if (topSection.PileSectionType == PileTypeNames.RcSection)
                {
                    issues.Add($"杭体 {no}");
                }
            }
            return issues;
        }

        [RelayCommand]
        private void OnCancel()
        {
            // ViewModel は自前の PileBodies (DeepCopy) を編集対象としており、
            // 編集中に InputModel.PileBodies は変更されない。
            // よってキャンセル時に InputModel.PileBodies を置き換える必要はない。
            // (旧実装は PropertyChanged を発火させて SoilPiles 再生成 → IsElementSplit=false の
            //  リセットを引き起こしていた)
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void AddSegment()
        {
            CurrentBody.PileBodySegments.Add(new PileBodySegment() { No = 1, });
            PileSection pileSection = CurrentBody.PileBodySegments[^1].PileSection;
            pileSection.PileBodyType = CurrentBody.PileBodyType;
            pileSection.ResetSectionProperties();
            RecalculateDataGridPileBody();
            DrawShapes();
            UpdateTemporarySoilPile();
        }

        [RelayCommand]
        public void OnPileBodyTextChanged(object parameter)
        {
            DrawShapes();
            UpdateTemporarySoilPile();
        }

        private static void UpdateViewModelCollection<T>(ObservableCollection<T> collection, int index, T value)
        {
            if (index >= 0 && index < collection.Count)
            {
                collection[index] = value;
            }
        }

        [RelayCommand]
        public static void OnTextBoxGotFocus(object parameter)
        {
            if (parameter is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void TextBoxPileBodyRef_LostFocus(object sender, RoutedEventArgs e)
        {
            var binding = ((TextBox)sender).GetBindingExpression(TextBox.TextProperty);
            binding?.UpdateSource(); // ViewModelのプロパティに値を反映
        }

        // 入力欄のフォーカスが外れたときの処理は code-behind の LostFocus ハンドラが持つ。
        // ViewModel 側にあった写しは参照が無かったので 2026-09-19 に撤去。

        // 杭頭編集メソッド
        [RelayCommand]
        private void EditPileHead(object parameter)
        {
            if (CurrentBody.PileBodySegments.Count > 0)
            {
                var pileTopWindow = new PileTopWindow(
                    _mainWindowViewModel,
                    CurrentBody.PileTop,
                    PileBodyNo,
                    CurrentBody.PileBodyType,
                    CurrentBody.PileTopType,
                    CurrentBody.PileConstructionType,
                    CurrentBody.PileBodySegments[0].PileSection);
                pileTopWindow.ShowDialog();
            }
            else
            {
                MessageService.Show("杭区間が存在しません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        //杭断面編集メソッド
        [RelayCommand]
        private void EditPileSection(object parameter)
        {
            var allowedTypes = new[]
            {
                PileTypeNames.InsituRc,
                PileTypeNames.InsituSteelPipeConcrete,
                PileTypeNames.PrecastConcrete,
                PileTypeNames.SteelPipe
            };

            var pileBody = CurrentBody;
            if (!allowedTypes.Contains(pileBody.PileBodyType)) return;
            if (parameter is not PileBodySegment segment) return;

            int i = pileBody.PileBodySegments.IndexOf(segment);
            if (i < 0) return;

            int segmentNo = i + 1;
            var pileSectionWindow = new PileSectionWindow(_mainWindowViewModel, segment.PileSection, PileBodyNo, segmentNo);
            bool? result = pileSectionWindow.ShowDialog();

            if (result == true)
            {
                pileBody.PileBodySegments[i].PileSection = pileSectionWindow.ViewModel.PileSection;
            }

            // 編集後に PileDescription プロパティの変更通知を発行
            OnPropertyChanged(nameof(PileBody.PileBodySegments));
            foreach (var seg in PileBody.PileBodySegments)
            {
                seg.PileSection.OnPropertyChanged(nameof(seg.PileSection.PileDescription));
            }

            // 最下段区間の断面が変わると Hybrid ニーディングの節部径 D1 が変わるので作り直す。
            // 断面ウィンドウの編集中は断面タイプが一時的に既定値へ戻ることがあり、
            // その途中の値のまま表示が固まらないよう、確定後にここで必ず再評価する。
            PileBody.UpdateHybridDerivedDiameter();

            DrawShapes();
            UpdateTemporarySoilPile();
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            // Run などの Visual でない要素も安全に扱えるよう LogicalTree フォールバックを併用
            DependencyObject parentObject;
            if (child is System.Windows.Media.Visual || child is System.Windows.Media.Media3D.Visual3D)
            {
                parentObject = VisualTreeHelper.GetParent(child);
            }
            else
            {
                parentObject = LogicalTreeHelper.GetParent(child);
            }
            if (parentObject == null) return null;

            if (parentObject is T parent)
            {
                return parent;
            }
            else
            {
                return FindVisualParent<T>(parentObject);
            }
        }

        [RelayCommand]
        public static void OnTextBoxPreviewMouseLeftButtonDown(object parameter)
        {
            if (parameter is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                textBox.Focus();
                // 既定の動作（キャレット移動など）を防ぐ場合は Handled を true にする
                if (Mouse.PrimaryDevice.LeftButton == MouseButtonState.Pressed)
                {
                    // イベント引数が取得できる場合のみ
                    if (Mouse.DirectlyOver is UIElement element)
                    {
                        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                            Source = textBox,
                            Handled = true
                        };
                        element.RaiseEvent(args);
                    }
                }
            }
        }

        // 断面の削除コマンドは置かない (どこからも辿れなかったので 2026-09-19 に撤去)。
        // 画面の「削除」は杭体 (DeletePileBodyCommand) と区間 (DeletePileBodySegmentCommand) を消す。

        [RelayCommand]
        private void RecalculateTipNonPermability(object parameter)
        {
            // PileBodyTypeプロパティが存在するかどうかを確認する
            if (CurrentBody.PileBodyType == PileTypeNames.PrecastConcrete &&
                CurrentBody.PileConstructionType == "打込み杭")
            {
                if (CurrentBody.PileTipStyle == "閉端杭")
                {
                    CurrentBody.TipNonPermability = 1.0;
                }
                else if (CurrentBody.PileTipStyle == "開端杭")
                {
                    double _lB = CurrentBody.EmbedmentIntoBearingSoil;
                    double _dI = CurrentBody.PileInnerDia;
                    if (_dI < 0.01) { _dI = 0.01; }
                    if (_lB < 0.01) { _lB = 0.01; }
                    if (_lB / _dI <= 5)
                    {
                        CurrentBody.TipNonPermability = 0.16 * (_lB / _dI);
                    }
                    else
                    {
                        CurrentBody.TipNonPermability = 0.80;
                    }
                }
            }
            if (CurrentBody.PileBodyType == PileTypeNames.SteelPipe &&
                CurrentBody.PileConstructionType == "回転貫入杭")
            {
                if (CurrentBody.PileTipStyle == "閉端杭")
                {
                    CurrentBody.TipNonPermability = 1.0;
                }
                else if (CurrentBody.PileTipStyle == "開端杭")
                {
                    CurrentBody.TipNonPermability = 0.80;
                }
            }
        }

        //public void ComboBoxPileBodyNo_SelectionChanged(
        //    int selectedPileBodyNo, int previousSelectedPileBodyNo)
        //{
        //    if (selectedPileBodyNo != 1 && selectedPileBodyNo == PileBodiesCountPlusOneList[^1])
        //    {
        //        PileBodies.Add(new PileBodyInput()
        //        {
        //            PileBodyRef = "(PB" + selectedPileBodyNo.ToString() + ")"
        //        });
        //        UpdatePileBodiesCountPlusOneList();
        //    }

        //    if (previousSelectedPileBodyNo != -1)
        //    {
        //        PileBody = CurrentBody;
        //    }

        //    DrawShapes();
        //    UpdateTemporarySoilPile();
        //}

        public void ComboBoxPileBodyNo_SelectionChanged(int selectedIndex)
        {
            if (selectedIndex < 0) return; // 未選択は無視

            // (New)が選択された場合
            if (selectedIndex == PileBodiesCountPlusOneList.Count - 1)
            {
                int newNo = PileBodies.Count + 1;
                PileBodies.Add(new PileBodyInput() { PileBodyRef = "(PB" + newNo.ToString() + ")" });
                UpdatePileBodiesCountPlusOneList();

                // ItemsSource更新後、必ず新しいアイテムを選択状態にする
                PileBodyNo = PileBodies.Count; // 1始まりなのでCount
                PileBody = PileBodies.Last();
            }
            else
            {
                if (selectedIndex >= 0 && selectedIndex < PileBodies.Count)
                {
                    PileBodyNo = selectedIndex + 1;
                    PileBody = PileBodies[selectedIndex];
                }
            }
            DrawShapes();
            UpdateTemporarySoilPile();
        }

        [RelayCommand]
        public void OnPileConstructionTypeSelectionChanged(object parameter)
        {
            // 杭工法変更時に杭姿図キャンバスを再描画
            // (回転貫入杭の螺旋羽根表示など、杭工法に依存する形状を反映)
            DrawShapes();
        }

        [RelayCommand]
        public void OnPileBodyTypeSelectionChanged(object parameter)
        {
            // すべての杭区間を削除
            CurrentBody.PileBodySegments.Clear();

            SetPileTopTypeAndConstruction();
            AddSegment(); // 1区間追加
        }

        [RelayCommand]
        private void DeletePileBodySegment(object parameter)
        {
            if (parameter is PileBodySegment selectedItem)
            {
                CurrentBody.PileBodySegments.Remove(selectedItem);
                RecalculateDataGridPileBody();
                DrawShapes();
                UpdateTemporarySoilPile();
            }
            else
            {
                // ここに来るのは実装の不具合。利用者に内部の型の話をしても操作は決まらないので、
                // ダイアログは出さずログに残す。
                Serilog.Log.Warning("選択項目の型が想定と異なるため処理をスキップしました");
            }
        }

        // 区間の削除は DeletePileBodySegmentCommand (画面の「区間削除」ボタン) が行う。
        // 選択行を消す版の写しは参照が無かったので 2026-09-19 に撤去。

        // 杭頭タイプと工法のセット
        private void SetPileTopTypeAndConstruction()
        {
            switch (CurrentBody.PileBodyType)
            {
                case PileTypeNames.InsituRc:
                    UpdatePileOptions(PileBodyInput.InsituReinforcedConcretePileTopTypeOption,
                        PileBodyInput.InsituPileConstructionTypeOption);
                    break;
                case PileTypeNames.InsituSteelPipeConcrete:
                    UpdatePileOptions(PileBodyInput.InsituSteelPipedConcretePileTopTypeOption,
                        PileBodyInput.InsituPileConstructionTypeOption);
                    break;
                case PileTypeNames.PrecastConcrete:
                    UpdatePileOptions(PileBodyInput.PrecastConcretePileTopTypeOption,
                        PileBodyInput.PrecastPileConstructionTypeOption);
                    break;
                case PileTypeNames.SteelPipe:
                    UpdatePileOptions(PileBodyInput.SteelPileTopTypeOption,
                        PileBodyInput.SteelPileConstructionTypeOption);
                    break;
            }
        }

        /// <summary>
        /// 現在の杭体タイプに基づいて PileTopTypeOption を再設定する（杭体番号切り替え時用）
        /// PileTopType は変更しない（既存の選択を維持）
        /// </summary>
        private void SyncPileTopTypeOption()
        {
            var body = CurrentBody;
            var correctOption = body.PileBodyType switch
            {
                PileTypeNames.InsituRc => PileBodyInput.InsituReinforcedConcretePileTopTypeOption,
                PileTypeNames.InsituSteelPipeConcrete => PileBodyInput.InsituSteelPipedConcretePileTopTypeOption,
                PileTypeNames.PrecastConcrete => PileBodyInput.PrecastConcretePileTopTypeOption,
                PileTypeNames.SteelPipe => PileBodyInput.SteelPileTopTypeOption,
                _ => PileBodyInput.InsituReinforcedConcretePileTopTypeOption
            };

            if (body.PileTopTypeOption != correctOption)
            {
                body.PileTopTypeOption = correctOption;
                // PileTopType が新しいオプションに含まれない場合のみデフォルトに戻す
                if (!correctOption.Contains(body.PileTopType))
                    body.PileTopType = correctOption[0];
            }

            // 施工法オプションも同期
            var correctConstruction = body.PileBodyType switch
            {
                PileTypeNames.InsituRc or PileTypeNames.InsituSteelPipeConcrete => PileBodyInput.InsituPileConstructionTypeOption,
                PileTypeNames.PrecastConcrete => PileBodyInput.PrecastPileConstructionTypeOption,
                PileTypeNames.SteelPipe => PileBodyInput.SteelPileConstructionTypeOption,
                _ => PileBodyInput.InsituPileConstructionTypeOption
            };

            if (body.PileConstructionTypeOption != correctConstruction)
            {
                body.PileConstructionTypeOption = correctConstruction;
                if (!correctConstruction.Contains(body.PileConstructionType))
                    body.PileConstructionType = correctConstruction[0];
            }
        }

        private void UpdatePileOptions(ObservableCollection<string> topTypeOption,
            ObservableCollection<string> constructionTypeOption)
        {
            if (CurrentBody.PileTopTypeOption != topTypeOption)
            {
                CurrentBody.PileTopTypeOption = topTypeOption;
                CurrentBody.PileTopType = topTypeOption[0];
            }

            if (CurrentBody.PileConstructionTypeOption != constructionTypeOption)
            {
                CurrentBody.PileConstructionTypeOption = constructionTypeOption;
                CurrentBody.PileConstructionType = constructionTypeOption[0];
            }
        }

        [RelayCommand]
        private void OnPileTopTypeSelectionChanged(object parameter)
        {
            // ViewModelのPileBodyNoプロパティを直接使用（ComboBoxは設定されていないため）
            int pileBodyNo = PileBodyNo;
            if (pileBodyNo <= 0 || pileBodyNo > PileBodies.Count) return;

            var selectedTopType = PileBody?.PileTopType;
            if (string.IsNullOrEmpty(selectedTopType)) return;

            BodyAt(pileBodyNo)!.PileTop.PileTopType = selectedTopType;

            // キャプテンパイル工法選択時にCaptainPileを作成してPCリングを自動選定
            if (selectedTopType == "キャプテンパイル工法")
            {
                var pileTop = BodyAt(pileBodyNo)!.PileTop;
                // CaptainPileが存在しない場合は作成
                if (pileTop.CaptainPile == null)
                {
                    pileTop.CaptainPile = new(pileTop.PileCapFc, pileTop.PileCapEc);
                }
                AutoSelectPCRing(pileBodyNo);
            }
            // FT-Pile構法選択時にFTPileを作成してFTキャップを自動選定
            else if (selectedTopType == "FT-Pile構法")
            {
                var pileTop = BodyAt(pileBodyNo)!.PileTop;
                // FTPileが存在しない場合は作成
                if (pileTop.FTPile == null)
                {
                    pileTop.FTPile = new(pileTop.PileCapFc, pileTop.PileCapEc);
                }
                AutoSelectFTCap(pileBodyNo);
            }
        }

        /// <summary>
        /// キャプテンパイル工法選択時にPCリングを自動選定
        /// セグメント0の杭径以上で最小のPCリング(-N)を選定
        /// </summary>
        private void AutoSelectPCRing(int pileBodyNo)
        {
            var pileBody = BodyAt(pileBodyNo)!;
            if (pileBody.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                return;

            // セグメント0の杭径を取得
            var segment0 = pileBody.PileBodySegments[0];
            double pileDia = segment0.PileSection?.PileDiameter ?? 0;
            if (pileDia <= 0) return;

            // 杭径以上で最小のPCリングサイズを計算
            // PCリングは800mmから3000mmまで100mm刻み
            int targetSize = (int)Math.Ceiling(pileDia / 100.0) * 100;
            targetSize = Math.Max(targetSize, 800);   // 最小800mm
            targetSize = Math.Min(targetSize, 3000);  // 最大3000mm

            // 標準タイプ名を生成 (例: "800-N", "1200-N")
            string targetPCRingName = $"{targetSize}-N";

            // CaptainPileのPCRingsから該当するPCRingを選択
            var captainPile = pileBody.PileTop?.CaptainPile;
            if (captainPile?.PCRings == null || captainPile.PCRings.Count == 0)
                return;

            var targetPCRing = captainPile.PCRings
                .FirstOrDefault(r => r.Name == targetPCRingName);

            if (targetPCRing != null)
            {
                // PCRingオブジェクトと選択名の両方を設定
                captainPile.PCRing = targetPCRing;
                captainPile.SelectedPCRingName = targetPCRingName;
                captainPile.D = targetPCRing.D;

                // Update()を呼ぶとCTPConcreteが作成され、SetBasicPropertiesも内部で呼ばれる
                captainPile.Update();

                // 引張定着筋のtD/tB最大値を更新
                captainPile.UpdateTDorTB();

                // 諸元表示を更新
                pileBody.PileTop.SelectedPileTopSpecification = targetPCRing.GetSpecs();

            }
            else
            {
            }
        }

        /// <summary>
        /// FT-Pile構法選択時にFTキャップを自動選定
        /// セグメント0の杭径に一致するFTキャップを選定
        /// </summary>
        private void AutoSelectFTCap(int pileBodyNo)
        {
            var pileBody = BodyAt(pileBodyNo)!;
            if (pileBody.PileBodySegments == null || pileBody.PileBodySegments.Count == 0)
                return;

            // セグメント0の杭径を取得
            var segment0 = pileBody.PileBodySegments[0];
            double pileDia = segment0.PileSection?.PileDiameter ?? 0;
            if (pileDia <= 0) return;

            // FTPileのFTCapsから該当するFTCapを選択
            var ftPile = pileBody.PileTop?.FTPile;
            if (ftPile?.FTCaps == null || ftPile.FTCaps.Count == 0)
                return;

            // 杭径に一致するFTキャップを検索（FTキャップは300-1200mm）
            // 完全一致がなければ、杭径以上で最小のものを選択
            var targetFTCap = ftPile.FTCaps.FirstOrDefault(c => Math.Abs(c.Phi - pileDia) < 1);
            if (targetFTCap == null)
            {
                // 完全一致がない場合、杭径以上で最小のものを選択
                targetFTCap = ftPile.FTCaps
                    .Where(c => c.Phi >= pileDia)
                    .OrderBy(c => c.Phi)
                    .FirstOrDefault();
            }
            // それでもない場合、最大のものを選択
            if (targetFTCap == null)
            {
                targetFTCap = ftPile.FTCaps.OrderByDescending(c => c.Phi).FirstOrDefault();
            }

            if (targetFTCap != null && pileBody.PileTop != null)
            {
                // FTCapを設定
                ftPile.FTCap = targetFTCap;
                ftPile.SelectedFTCapName = targetFTCap.Phi.ToString();
                pileBody.PileTop.SelectedFTCap = (int)targetFTCap.Phi;

                // 杭の寸法を設定（外径と内径）
                // 杭径は杭断面から。導出は FTPile.DimensionsFromSection に集約
                ftPile.SetDimensionsFromSection(
                    pileDia, segment0.PileSection?.ConcreteThickness ?? 0.0);

                // FTPileを更新
                ftPile.Update();

                // 諸元表示を更新
                pileBody.PileTop.SelectedPileTopSpecification = targetFTCap.GetSpecs();

            }
            else
            {
            }
        }

        [RelayCommand]
        private void OnPresetSettlementParametersChanged(object sender)

        {
            if (ComboBoxPresetSettlementParameters == null) return;

            var selectedPresetParameter = ComboBoxPresetSettlementParameters.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(selectedPresetParameter)) return;

            foreach (PileBodyInput.PileTipSettlementPresetParameter parameter in
                CurrentBody.PileTipSettlementPresetParameters)
            {
                if (selectedPresetParameter.Contains(parameter.Name) &&
                    selectedPresetParameter.Contains(parameter.SoilType))
                {
                    CurrentBody.SettleAlpha = parameter.Alpha;
                    CurrentBody.SettleN = parameter.N;
                    break;
                }
            }
            //AddComponent(CurrentBody.SettleAlpha, CurrentBody.SettleN);
            //DrawShapes(Canvas);
        }

        //杭先端沈下チャート要素追加コマンド
        //[RelayCommand]
        //public static void AddComponent(double alpha, double n)
        //{

        //}

        public void RecalculateDataGridPileBody()
        {
            double _sum = 0;
            for (int i = 0; i < CurrentBody.PileBodySegments.Count; i++)
            {
                _sum += CurrentBody.PileBodySegments[i].SegmentLength;
                CurrentBody.PileBodySegments[i].SegmentDepth = _sum;
                CurrentBody.PileBodySegments[i].No = i + 1;
            }
            OnRecalculateDataGridPileBodyCompleted(EventArgs.Empty); // イベントを発生させる
        }


        public void DrawShapes()
        {
            // 安全な範囲チェック
            if (PileBodies == null || PileBodies.Count == 0 || PileBodyNo <= 0 || PileBodyNo > PileBodies.Count)
                return;

            double pileTopAltitude = 0;
            GroundInput groundInput = null;

            if (IsGroundLayerOverwrapped)
            {
                pileTopAltitude = PileTopAltitude;
                // ガード: SelectedGroundNo が範囲外の場合は安全な値にクランプ
                if (InputModel?.GroundsInput != null && InputModel.GroundsInput.Count > 0)
                {
                    int gIdx = SelectedGroundNo - 1;
                    if (gIdx < 0) gIdx = 0;
                    if (gIdx >= InputModel.GroundsInput.Count) gIdx = InputModel.GroundsInput.Count - 1;
                    groundInput = InputModel.GroundsInput[gIdx];
                }
            }


            ShapeDrawer.DrawPileElevation(
                Canvas,
                CurrentBody.PileBodySegments,
                CurrentBody.PileToeDia,
                CurrentBody.InsituPileToeHeight,
                CurrentBody.InsituPileToeAngle,
                CurrentBody.PrecastConcretePileToeHeightRatio,
                CurrentBody.PileConstructionType,
                pileTopAltitude,
                groundInput,
                showLiquefactionFL: IsLiquefactionFLVisible,
                showGroundDisplacement: IsGroundDisplacementVisible,
                seismicLevelIndex: SelectedSeismicLevelIndex,
                displacementWithLiquefaction: IsDisplacementWithLiquefaction,
                smartMagnumLL: CurrentBody.SmartMagnumLL,
                smartMagnumDes: CurrentBody.SmartMagnumDes,
                smartMagnumWingLength: CurrentBody.SmartMagnumWingLength,
                hybridE: CurrentBody.HybridExpansionRatio,
                hybridEs: CurrentBody.HybridExcavationRatio,
                hybridLu: CurrentBody.HybridPileBelowLength);
        }
    }
}



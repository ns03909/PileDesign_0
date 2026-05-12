using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public partial class FoundationBeamViewModel : BaseViewModel
    {
        private readonly MainWindowViewModel _mainWindowViewModel;
        public InputModel InputModel => _mainWindowViewModel.CurrentInputModel;

        // キャンセル時に復元するためのスナップショット (Initialize で取得)
        private FoundationBeamInput? _prevFoundationBeamInput;

        // 基礎梁節点コレクション
        [ObservableProperty]
        private ObservableCollection<FoundationNode> _nodes = [];

        // 基礎梁コレクション
        [ObservableProperty]
        private ObservableCollection<FoundationBeam> _beams = [];

        // 接続モード
        [ObservableProperty]
        private FoundationBeamConnectionMode _connectionMode = FoundationBeamConnectionMode.RigidFloor;

        // 選択された節点・要素
        [ObservableProperty]
        private FoundationNode? _selectedNode;

        [ObservableProperty]
        private FoundationBeam? _selectedBeam;

        // RequestClose イベント
        public event EventHandler? RequestClose;

        // コマンド
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddNodeCommand { get; }
        public ICommand AddBeamCommand { get; }

        // コンストラクタ
        public FoundationBeamViewModel(MainWindowViewModel mainWindowViewModel)
        {
            _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

            OkCommand = new RelayCommand(_ => OnOk());
            CancelCommand = new RelayCommand(_ => OnCancel());
            AddNodeCommand = new RelayCommand(_ => OnAddNode());
            AddBeamCommand = new RelayCommand(_ => OnAddBeam());
        }

        public void Initialize()
        {
            // InputModel から基礎梁データを読み込む
            if (InputModel.FoundationBeamInput == null)
            {
                InputModel.FoundationBeamInput = new FoundationBeamInput();
            }

            var fbInput = InputModel.FoundationBeamInput;

            // キャンセル時の復元用スナップショットを取得
            // (Nodes/Beams は参照コピーで直接編集する設計のため、バックアップが必要)
            _prevFoundationBeamInput = DeepCopyUtil.CloneJson(fbInput);

            // 参照をコピー（直接編集）
            Nodes = fbInput.Nodes;
            Beams = fbInput.Beams;
            ConnectionMode = fbInput.ConnectionMode;

            // 節点番号・要素番号を自動更新
            RenumberNodes();
            RenumberBeams();
        }

        private void OnOk()
        {
            // データを InputModel に保存
            if (InputModel.FoundationBeamInput != null)
            {
                InputModel.FoundationBeamInput.Nodes = Nodes;
                InputModel.FoundationBeamInput.Beams = Beams;
                InputModel.FoundationBeamInput.ConnectionMode = ConnectionMode;
            }

            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void OnCancel()
        {
            // Initialize で取得したスナップショットに戻す。
            // Nodes/Beams は参照コピーで直接編集する設計のため、
            // InputModel.FoundationBeamInput 自体を差し替えて変更を破棄する。
            if (_prevFoundationBeamInput != null)
            {
                InputModel.FoundationBeamInput = _prevFoundationBeamInput;
            }
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private void OnAddNode()
        {
            var newNode = new FoundationNode
            {
                No = Nodes.Count + 1,
                X = 0.0,
                Y = 0.0,
                Z = 0.0,
                Name = $"Node-{Nodes.Count + 1}"
            };
            Nodes.Add(newNode);
        }

        public void DeleteSelectedNode()
        {
            if (SelectedNode != null && Nodes.Contains(SelectedNode))
            {
                Nodes.Remove(SelectedNode);
                RenumberNodes();
            }
        }

        private void OnAddBeam()
        {
            // No プロパティは廃止 (位置 = ID)
            var newBeam = new FoundationBeam
            {
                MaterialNo = 1,
                SectionNo = 1,
                SectionName = $"Beam-{Beams.Count + 1}",
                Width = 0.5,
                Height = 0.8,
                YoungModulus = 2.6589e7,
                ShearModulus = 1.1079e7
            };
            Beams.Add(newBeam);

            // MaterialNo=1 / SectionNo=1 の参照先を保証
            InputModel.FoundationBeamInput?.EnsureDefaultMaterialAndSection();
        }

        public void DeleteSelectedBeam()
        {
            if (SelectedBeam != null && Beams.Contains(SelectedBeam))
            {
                Beams.Remove(SelectedBeam);
                RenumberBeams();
            }
        }

        private void RenumberNodes()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                Nodes[i].No = i + 1;
            }
        }

        private void RenumberBeams()
        {
            // No-op: FoundationBeam.No は廃止 (位置 = ID)
        }
    }
}

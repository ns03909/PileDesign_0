using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PileDesign.Models
{
    // 単純なランプ状態モデル（インデックスとオン/オフ）
    public class LampState : INotifyPropertyChanged
    {
        private bool _isOn;
        public int Index { get; set; }
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                OnPropertyChanged();
            }
        }

        public LampState(int index, bool isOn = false)
        {
            Index = index;
            _isOn = isOn;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
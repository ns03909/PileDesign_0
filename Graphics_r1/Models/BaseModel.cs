//using CommunityToolkit.Mvvm.ComponentModel;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Runtime.CompilerServices;


//namespace PileDesign.Models
//{
//    public class BaseModel : ObservableObject, INotifyPropertyChanged
//    {
//        // INotifyPropertyChanged を実装するためのイベントハンドラ
//        public event PropertyChangedEventHandler PropertyChanged;

//        // プロパティ名によって自動的にセットされる
//        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

//        // 新たに追加されたコマンドを処理するメソッド
//        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
//        {
//            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
//            field = value;
//            OnPropertyChanged(propertyName);
//            return true;
//        }
//    }
//}
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PileDesign.Models
{
    public class BaseModel : ObservableObject, INotifyPropertyChanged, INotifyDataErrorInfo
    {
        public new event PropertyChangedEventHandler PropertyChanged;

        public new virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected new bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // ---- 追加: 数値バリデーション支援 ---------------------------------

        // エラー格納
        private readonly Dictionary<string, List<string>> _errors = [];

        // INotifyDataErrorInfo
        public bool HasErrors => _errors.Count > 0;
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return null;
            return _errors.TryGetValue(propertyName, out var list) ? list : null;
        }

        protected void AddError(string propertyName, string message)
        {
            if (!_errors.ContainsKey(propertyName))
                _errors[propertyName] = [];
            if (!_errors[propertyName].Contains(message))
            {
                _errors[propertyName].Add(message);
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            }
        }

        protected void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        // 追加: Infinity / NaN を拒否 or 置換する double 用汎用 setter
        protected bool SetFiniteDouble(
            ref double field,
            double value,
            double fallback = 0.0,
            bool recordError = true,
            [CallerMemberName] string propertyName = null)
        {
            bool invalid = double.IsNaN(value) || double.IsInfinity(value);
            if (invalid)
            {
                value = fallback;
                if (recordError)
                    AddError(propertyName, "非有限値 (NaN/Infinity) を検出し既定値へ置換しました。");
            }
            else
            {
                if (recordError) ClearErrors(propertyName);
            }
            return SetProperty(ref field, value, propertyName);
        }

        // 追加: 範囲制限付き (例: 下限0)
        protected bool SetFiniteClampedDouble(
            ref double field,
            double value,
            double min = double.NegativeInfinity,
            double max = double.PositiveInfinity,
            double fallback = 0.0,
            bool recordError = true,
            [CallerMemberName] string propertyName = null)
        {
            bool invalid = double.IsNaN(value) || double.IsInfinity(value);
            bool clamped = false;
            if (invalid)
            {
                value = fallback;
                if (recordError)
                    AddError(propertyName, "非有限値を検出し既定値へ置換しました。");
            }
            if (value < min || value > max)
            {
                value = Math.Min(Math.Max(value, min), max);
                clamped = true;
                if (recordError)
                    AddError(propertyName, $"許容範囲 [{min}, {max}] 外のため補正しました。");
            }
            // invalid でもクランプでもなく、過去のエラーが残っているなら解消通知
            if (recordError && !invalid && !clamped && _errors.ContainsKey(propertyName))
            {
                ClearErrors(propertyName);
            }
            return SetProperty(ref field, value, propertyName);
        }
    }
}
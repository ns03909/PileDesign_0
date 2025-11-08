//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Runtime.CompilerServices;

//namespace PileDesign.ViewModels
//{
//    [Serializable]
//    public partial class BaseDataItem : INotifyPropertyChanged
//    {
//        // INotifyPropertyChanged を実装するためのイベントハンドラ
//        public event PropertyChangedEventHandler PropertyChanged;

//        // プロパティ名によって自動的にセットされる
//        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
//            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

//        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
//        {
//            if (EqualityComparer<T>.Default.Equals(storage, value))
//            {
//                return false; // プロパティの値が変更されなかった場合はfalseを返す
//            }

//            storage = value;
//            OnPropertyChanged(propertyName);
//            return true; // プロパティの値が変更された場合はtrueを返す
//        }
//    }
//}
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PileDesign.ViewModels
{
    [Serializable]
    public partial class BaseDataItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value))
                return false;
            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        // --- 追加: 数値バリデーション支援 ---

        protected bool SetFiniteDouble(
            ref double storage,
            double value,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = fallback;
            return SetProperty(ref storage, value, propertyName);
        }

        protected bool SetFiniteClampedDouble(
            ref double storage,
            double value,
            double min = double.NegativeInfinity,
            double max = double.PositiveInfinity,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = fallback;
            }
            if (value < min) value = min;
            if (value > max) value = max;
            return SetProperty(ref storage, value, propertyName);
        }
    }
}
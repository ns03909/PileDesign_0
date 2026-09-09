//using System;
//using System.Collections.Generic;
//using System.ComponentModel;
//using System.Runtime.CompilerServices;

//namespace PileDesign.Common
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

namespace PileDesign.Common
{
    /// <summary>
    /// 表の 1 行や入力項目の土台。変更通知と数値の検査だけを持つ。
    ///
    /// 画面固有のものは何も入っていないが、以前は ViewModels 名前空間にいた。
    /// そのため入力データ側の 13 ファイルが「画面の名前空間」を using しており、
    /// <b>下の層が画面に依存しているように見えて</b>いた。本当の違反が埋もれるので移した。
    /// </summary>
    [Serializable]
    public partial class BaseDataItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PileDesign.Common
{
    /// <summary>
    /// 変更通知と数値の検査だけを持つ土台。<b>画面のものは何も持たない。</b>
    ///
    /// 以前はこれが <c>ViewModels.BaseViewModel</c> の中にあり、表のコピー命令
    /// (DataGrid を触る) と同居していた。そのため入力データ側のクラスが
    /// これを継承するために「画面の名前空間」を using することになり、
    /// <b>下の層が画面に依存しているように見えて</b>いた。本当の違反が埋もれるので分けた。
    ///
    /// 画面側は <c>ViewModels.BaseViewModel</c> を使う。あちらはこれを継承して
    /// 表のコピー命令を足したもの。
    /// </summary>
    [Serializable]
    public partial class ObservableModel : ObservableObject
    {
        // ObservableObject が持つ変更通知のデリゲート (field-like event の裏のフィールド)。
        // 派生クラスからは null にできないので、複製の購読者を外すときだけ名前で引く。
        // 名前が変わった (ツールキットの更新) ことは ObservableModelCloneTests が見張る
        internal static readonly System.Reflection.FieldInfo? PropertyChangedField =
            typeof(ObservableObject).GetField(nameof(PropertyChanged), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        internal static readonly System.Reflection.FieldInfo? PropertyChangingField =
            typeof(ObservableObject).GetField(nameof(PropertyChanging), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// <see cref="object.MemberwiseClone"/> の代わりに使う複製。値はそのまま写し、変更通知の購読者は写さない
        /// (MemberwiseClone はデリゲートも写すので、複製を変えると元を見ている画面へ通知が飛ぶ)。
        /// </summary>
        protected T CloneWithoutSubscribers<T>() where T : ObservableModel
        {
            var copy = (T)MemberwiseClone();
            PropertyChangedField?.SetValue(copy, null);
            PropertyChangingField?.SetValue(copy, null);
            return copy;
        }

        // ObservableObject の OnPropertyChanged を呼び出す
        public new virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            base.OnPropertyChanged(propertyName);

        // SetProperty は自前で保持しつつ、通知は base.OnPropertyChanged に委譲
        protected new bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            base.OnPropertyChanged(propertyName);
            return true;
        }

        // --- 数値バリデーション支援 ---

        protected bool SetFiniteDouble(
            ref double field,
            double value,
            double fallback = 0.0,
            [CallerMemberName] string propertyName = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = fallback;
            return SetProperty(ref field, value, propertyName);
        }

        protected bool SetFiniteClampedDouble(
            ref double field,
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
            return SetProperty(ref field, value, propertyName);
        }
    }
}

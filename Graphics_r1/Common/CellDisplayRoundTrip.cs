using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PileDesign.Common
{
    /// <summary>
    /// 表のセルを<b>触っただけ</b>で、表示用に丸めた値が入力に書き戻されないようにする。
    ///
    /// <para>数値の列は <c>StringFormat</c> (N1・0.000 など) で丸めて表示している。WPF の表は、編集を始めると
    /// 編集用の欄にも同じ書式の文字 (例: 123.456 → 「123.5」) を入れ、確定するとその文字を読み戻して書き込む。
    /// そのため、セルに入って何も打たずに Enter / Tab で抜けただけで、入力が 123.5 に変わっていた
    /// (画面の見た目は変わらないので気づけない。解析には丸めた値が使われる)。</para>
    ///
    /// <para>以前は沈下用土層の表と荷重面等価径の列だけが個別に対策しており、他の入力表 (地盤・杭体・荷重・要素分割など)
    /// は丸めたまま書き戻していた。ここでは <see cref="EnhancedDataGrid"/> がすべての表で行う:
    /// 編集を始めたときの文字を覚えておき、確定するとき (または欄から外れて書き戻しが起きる前) に文字が
    /// 変わっていなければ、欄の文字を<b>元の値を丸めずに書いた文字</b>に差し替える。書き戻されるのは元と同じ値なので、
    /// 入力は変わらない (多くのプロパティは同じ値の代入を変更として扱わないので、履歴も増えない)。</para>
    ///
    /// <para>利用者が丸めた表示と同じ文字を打ち直した場合も「変わっていない」とみなし、元の値を残す
    /// (表示と同じ文字を打つのは、値を変えるつもりがないときと区別できない)。</para>
    /// </summary>
    internal static class CellDisplayRoundTrip
    {
        // 編集を始めたときの欄の文字。編集用の欄は編集を始めるたびに作り直されるので、欄ごとに持つ
        private static readonly ConditionalWeakTable<TextBox, string> StartTexts = new();

        /// <summary>
        /// 編集を始めたときの文字を覚える (丸めた書式の欄だけ)。
        /// 欄から外れたときに書き戻す欄 (UpdateSourceTrigger=LostFocus) は、確定より先に書き戻しが起きうるので、外れる直前にも差し替える。
        /// </summary>
        internal static void Remember(FrameworkElement? editingElement)
        {
            if (editingElement is not TextBox box || !IsProtected(box)) return;
            StartTexts.AddOrUpdate(box, box.Text ?? string.Empty);
            box.PreviewLostKeyboardFocus -= OnLeaving;
            box.PreviewLostKeyboardFocus += OnLeaving;
        }

        private static void OnLeaving(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box) KeepOriginalIfUntouched(box);
        }

        /// <summary>
        /// 欄の文字が編集を始めたときのままなら、元の値を丸めずに書いた文字に差し替える。差し替えたら true。
        /// </summary>
        internal static bool KeepOriginalIfUntouched(FrameworkElement? editingElement)
        {
            if (editingElement is not TextBox box) return false;
            if (!StartTexts.TryGetValue(box, out var start)) return false;
            if (!string.Equals(box.Text ?? string.Empty, start, StringComparison.Ordinal)) return false;

            var expression = box.GetBindingExpression(TextBox.TextProperty);
            if (expression?.ParentBinding is not Binding binding || !IsRoundingBinding(binding)) return false;
            if (!TryReadSourceValue(box, binding, out object? value)) return false;

            var culture = binding.ConverterCulture
                          ?? box.Language?.GetSpecificCulture()
                          ?? CultureInfo.InvariantCulture;
            string? exact = ExactText(value, culture);
            if (exact == null || string.Equals(exact, box.Text, StringComparison.Ordinal)) return false;

            box.Text = exact;
            StartTexts.AddOrUpdate(box, exact);   // 同じ編集で 2 度目に呼ばれても (外れる → 確定) 同じ扱いにする
            return true;
        }

        /// <summary>この欄は保護の対象か (丸める書式で、値をそのまま書き戻すバインディング)。</summary>
        private static bool IsProtected(TextBox box)
            => box.GetBindingExpression(TextBox.TextProperty)?.ParentBinding is Binding b && IsRoundingBinding(b);

        /// <summary>
        /// 書式で丸めて表示し、そのまま書き戻すバインディングか。変換器を挟むもの (表示と値の形が違う) は扱わない。
        /// 行のデータを起点にしないもの (Source / RelativeSource / ElementName) も、元の値を読み直せないので扱わない。
        /// </summary>
        internal static bool IsRoundingBinding(Binding binding)
            => !string.IsNullOrEmpty(binding.StringFormat)
               && binding.Converter == null
               && binding.Mode is BindingMode.Default or BindingMode.TwoWay
               && binding.Source == null && binding.RelativeSource == null && string.IsNullOrEmpty(binding.ElementName)
               && binding.Path != null && !string.IsNullOrEmpty(binding.Path.Path);

        /// <summary>
        /// いま入っている値をバインディングの道筋どおりに読み直す (入れ子のパスや添字にも同じ規則で届く)。
        /// </summary>
        private static bool TryReadSourceValue(TextBox box, Binding binding, out object? value)
        {
            value = null;
            if (box.DataContext == null) return false;
            var probe = new ValueProbe();
            try
            {
                BindingOperations.SetBinding(probe, ValueProbe.ValueProperty,
                    new Binding { Path = binding.Path, Source = box.DataContext, Mode = BindingMode.OneTime });
                value = probe.GetValue(ValueProbe.ValueProperty);
                return value != null && value != DependencyProperty.UnsetValue;
            }
            finally
            {
                BindingOperations.ClearBinding(probe, ValueProbe.ValueProperty);
            }
        }

        /// <summary>
        /// 値を丸めずに書いた文字 (読み戻すと同じ値になる)。数値でない・有限でないときは null (扱わない)。
        /// </summary>
        internal static string? ExactText(object? value, CultureInfo culture) => value switch
        {
            double d when double.IsFinite(d) => d.ToString("R", culture),
            float f when float.IsFinite(f) => f.ToString("R", culture),
            decimal m => m.ToString(culture),
            int i => i.ToString(culture),
            long l => l.ToString(culture),
            short s => s.ToString(culture),
            _ => null,
        };

        private sealed class ValueProbe : DependencyObject
        {
            public static readonly DependencyProperty ValueProperty =
                DependencyProperty.Register("Value", typeof(object), typeof(ValueProbe));
        }
    }
}

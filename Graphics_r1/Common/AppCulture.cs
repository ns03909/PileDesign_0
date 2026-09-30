using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace PileDesign.Common
{
    /// <summary>
    /// アプリの地域設定を ja-JP (Windows の地域設定による上書きなし) に固定する。起動時に 1 回。
    ///
    /// <para>以前は何も決めておらず、場所ごとに違う地域設定で数値を扱っていた。</para>
    /// <list type="bullet">
    /// <item>コードの中の数値の文字列化・読み取り (<c>ToString("N3")</c>・<c>double.Parse</c> など) は Windows の地域設定</item>
    /// <item>画面の入力欄 (WPF のバインディング) は、既定の en-US</item>
    /// </list>
    /// <para>小数点がカンマの地域 (ドイツ語など) や、コントロールパネルで小数点・桁区切りを変えた PC では、表示・読み取りが
    /// 食い違った (「1.234,5」と「1,234.5」が同じ画面に並ぶ、入力した値を別の値と読む)。画面・計算書・単位はすべて日本語なので、
    /// どの PC でも同じ扱いになるよう ja-JP に揃える。保存ファイル (JSON) はもともと地域設定に依らない。</para>
    /// </summary>
    internal static class AppCulture
    {
        internal const string Name = "ja-JP";

        /// <summary>
        /// いまのスレッドと、これから作られるスレッドの地域設定を ja-JP にする。
        /// Windows の地域設定で変えた小数点・桁区切りは使わない (<c>useUserOverride: false</c>)。
        /// </summary>
        internal static CultureInfo Apply()
        {
            var culture = new CultureInfo(Name, useUserOverride: false);
            culture = CultureInfo.ReadOnly(culture);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return culture;
        }

        private static bool _wpfApplied;

        /// <summary>
        /// WPF の表示・入力 (バインディングの書式と読み取り) の言語も ja-JP にする。WPF の既定は en-US。
        /// 型の既定値の上書きはプロセスで 1 回しかできないので、2 回目以降は何もしない。
        /// </summary>
        internal static void ApplyToWpf()
        {
            if (_wpfApplied) return;
            _wpfApplied = true;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Name)));
        }
    }
}

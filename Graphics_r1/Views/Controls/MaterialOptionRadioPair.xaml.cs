using System.Windows;
using System.Windows.Controls;

namespace PileDesign.Views.Controls
{
    /// <summary>
    /// 基本設定のモデル化オプション 1 項目分（見出し＋ヘルプリンク＋「既定 vs 代替」のラジオペア）。
    ///
    /// FundamentalWindow に 9 回繰り返されていた同型 XAML（約 25 行×9）をテンプレート化したもの。
    /// - 既定側ラジオは <see cref="IsAlternativeSelected"/> の否定にバインド（片方を選ぶともう片方が外れる）
    /// - 見出しとヘルプリンクは <see cref="AreRadiosEnabled"/>=false（グレーアウト）でも操作可能
    /// - 式サブテキスト（Formula）は未指定なら行ごと非表示
    /// - HelpAnchor は help.html の id（HelpAnchorTests が実在を検査する）
    /// </summary>
    public partial class MaterialOptionRadioPair : UserControl
    {
        public MaterialOptionRadioPair()
        {
            InitializeComponent();
        }

        /// <summary>項目見出し（例:「ヤング係数 Ec の算定」）</summary>
        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>RadioButton のグループ名（項目ごとに一意にする）</summary>
        public string GroupName
        {
            get => (string)GetValue(GroupNameProperty);
            set => SetValue(GroupNameProperty, value);
        }
        public static readonly DependencyProperty GroupNameProperty =
            DependencyProperty.Register(nameof(GroupName), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>help.html のアンカー id（「ヘルプ」リンクの遷移先）</summary>
        public string HelpAnchor
        {
            get => (string)GetValue(HelpAnchorProperty);
            set => SetValue(HelpAnchorProperty, value);
        }
        public static readonly DependencyProperty HelpAnchorProperty =
            DependencyProperty.Register(nameof(HelpAnchor), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>既定（基礎部材の強度と変形性能）側のラベル</summary>
        public string DefaultText
        {
            get => (string)GetValue(DefaultTextProperty);
            set => SetValue(DefaultTextProperty, value);
        }
        public static readonly DependencyProperty DefaultTextProperty =
            DependencyProperty.Register(nameof(DefaultText), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>既定側の式サブテキスト（未指定なら非表示）</summary>
        public string DefaultFormula
        {
            get => (string)GetValue(DefaultFormulaProperty);
            set => SetValue(DefaultFormulaProperty, value);
        }
        public static readonly DependencyProperty DefaultFormulaProperty =
            DependencyProperty.Register(nameof(DefaultFormula), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>既定側のツールチップ（string または任意のコンテンツ）</summary>
        public object? DefaultToolTip
        {
            get => GetValue(DefaultToolTipProperty);
            set => SetValue(DefaultToolTipProperty, value);
        }
        public static readonly DependencyProperty DefaultToolTipProperty =
            DependencyProperty.Register(nameof(DefaultToolTip), typeof(object), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(null));

        /// <summary>代替オプション側のラベル</summary>
        public string AlternativeText
        {
            get => (string)GetValue(AlternativeTextProperty);
            set => SetValue(AlternativeTextProperty, value);
        }
        public static readonly DependencyProperty AlternativeTextProperty =
            DependencyProperty.Register(nameof(AlternativeText), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>代替側の式サブテキスト（未指定なら非表示）</summary>
        public string AlternativeFormula
        {
            get => (string)GetValue(AlternativeFormulaProperty);
            set => SetValue(AlternativeFormulaProperty, value);
        }
        public static readonly DependencyProperty AlternativeFormulaProperty =
            DependencyProperty.Register(nameof(AlternativeFormula), typeof(string), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(string.Empty));

        /// <summary>代替側のツールチップ（string または任意のコンテンツ）</summary>
        public object? AlternativeToolTip
        {
            get => GetValue(AlternativeToolTipProperty);
            set => SetValue(AlternativeToolTipProperty, value);
        }
        public static readonly DependencyProperty AlternativeToolTipProperty =
            DependencyProperty.Register(nameof(AlternativeToolTip), typeof(object), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(null));

        /// <summary>代替オプションが選択されているか（VM の bool オプションに TwoWay バインドする）</summary>
        public bool IsAlternativeSelected
        {
            get => (bool)GetValue(IsAlternativeSelectedProperty);
            set => SetValue(IsAlternativeSelectedProperty, value);
        }
        public static readonly DependencyProperty IsAlternativeSelectedProperty =
            DependencyProperty.Register(nameof(IsAlternativeSelected), typeof(bool), typeof(MaterialOptionRadioPair),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>ラジオ 2 つの有効/無効（見出し・ヘルプリンクは常に有効のまま）</summary>
        public bool AreRadiosEnabled
        {
            get => (bool)GetValue(AreRadiosEnabledProperty);
            set => SetValue(AreRadiosEnabledProperty, value);
        }
        public static readonly DependencyProperty AreRadiosEnabledProperty =
            DependencyProperty.Register(nameof(AreRadiosEnabled), typeof(bool), typeof(MaterialOptionRadioPair),
                new PropertyMetadata(true));

        // 「ヘルプ」リンク: help.html の HelpAnchor 位置へスクロールして開く
        private void HelpLink_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(HelpAnchor))
                ViewModels.MainWindowViewModel.OpenHelpWindowAt(HelpAnchor, null);
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestProject1
{
    /// <summary>
    /// テストが触った静的な状態を、元に戻す。
    ///
    /// 材料モデル化オプション (<see cref="ConcreteModelOptions"/>) は 15 個の静的な値で、
    /// 本番では基本設定から 1 か所でまとめて書かれる。テストは個別に書き換えるが、
    /// 戻し忘れると<b>次のテストがその値で走る</b>。
    /// 実際に、収束回帰のテストには戻す処理が無く、
    /// 「全 static を退避する」と書かれた別の仕組みも 15 個のうち 13 個しか見ていなかった。
    /// これが「単体では通るのに全体実行で落ちる」の主な原因。
    ///
    /// <b>退避する項目を手で並べない。</b> 並べると、オプションを足したときに
    /// ここへ足し忘れて同じことが起きる。設定できる静的プロパティを反射で数え上げる。
    ///
    /// <example>
    /// <code>
    /// using var _ = TestStateScope.Enter();
    /// ConcreteModelOptions.UseFiberMPhi = true;
    /// // ここを抜けると元に戻る
    /// </code>
    /// </example>
    /// </summary>
    internal sealed class TestStateScope : IDisposable
    {
        private readonly List<(PropertyInfo Prop, object? Value)> _saved = [];
        private readonly PileDesign.ViewModels.MainWindowViewModel? _savedMainViewModel;
        private readonly bool _savedVariationMode;

        private TestStateScope()
        {
            foreach (var p in SettableStaticOptions())
                _saved.Add((p, p.GetValue(null)));

            _savedMainViewModel = PileDesign.App.CurrentMainViewModel;
            _savedVariationMode = PileDesign.Common.AxialForceModeContext.IsVariationMode;
        }

        /// <summary>退避を始める。<c>using</c> で受けること。</summary>
        internal static TestStateScope Enter() => new();

        public void Dispose()
        {
            // 逆順に戻す。UseNotification1113 のように、書くと他の 2 つも書き換える
            // 「まとめ役」があるので、先に書いたものが後から上書きされないようにする。
            for (int i = _saved.Count - 1; i >= 0; i--)
                _saved[i].Prop.SetValue(null, _saved[i].Value);

            PileDesign.App.CurrentMainViewModel = _savedMainViewModel;
            PileDesign.Common.AxialForceModeContext.IsVariationMode = _savedVariationMode;
        }

        /// <summary>
        /// 設定できる静的オプション。読み取り専用 (<c>Version</c> や導出値) は対象外。
        /// </summary>
        internal static IEnumerable<PropertyInfo> SettableStaticOptions() =>
            typeof(ConcreteModelOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.CanRead && p.CanWrite)
                .OrderBy(p => p.Name, StringComparer.Ordinal);
    }
}

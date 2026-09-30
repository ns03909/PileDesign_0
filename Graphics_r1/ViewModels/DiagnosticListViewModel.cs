using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PileDesign.Common;
using PileDesign.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.ViewModels
{
    /// <summary>診断の一覧の 1 行。</summary>
    public sealed class DiagnosticRow
    {
        public DiagnosticRow(Diagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public Diagnostic Diagnostic { get; }
        public string SeverityLabel => Common.Diagnostic.SeverityLabel(Diagnostic.Severity);
        public string Location => Diagnostic.Target.Label;
        public string Message => Diagnostic.Message;
        public string Remedy => DiagnosticSelection.RemedyOf(Diagnostic) ?? "";
    }

    /// <summary>
    /// 入力の診断の一覧 (モードレスのウィンドウ)。解析前の検査・入力の注意・番号の参照の検査をまとめて重い順に並べ、
    /// 選んだ指摘の場所 (杭・杭体・区間・地盤) へ移動する。直したら再検査して、解消した指摘を一覧から除く。
    ///
    /// <para>以前は解析を始めたときの知らせ (1 回きりのダイアログ) でしか指摘を見られず、複数の指摘を順に直すと、
    /// どれが残っているかを追えなかった。</para>
    /// </summary>
    public partial class DiagnosticListViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _main;

        public DiagnosticListViewModel(MainWindowViewModel main)
        {
            _main = main;
            Recheck();
        }

        public ObservableCollection<DiagnosticRow> Rows { get; } = [];

        [ObservableProperty]
        private DiagnosticRow? _selectedRow;

        /// <summary>一覧の上に出す状態 (件数・前回から解消した件数)。</summary>
        [ObservableProperty]
        private string _statusText = "";

        /// <summary>いまの入力の指摘をすべて集める (重い順・同じ文は 1 つ)。</summary>
        internal static List<Diagnostic> Collect(Models.InputData.InputModel? input, IEnumerable<Diagnostic>? extra = null)
        {
            if (input == null) return [];
            var all = new List<Diagnostic>(extra ?? []);
            all.AddRange(ReferenceIntegrity.Check(input).All);
            all.AddRange(CheckInputData.CollectAnalysisBlockers(input));
            all.AddRange(CheckInputData.CollectInputWarningDiagnostics(input));
            var seen = new HashSet<string>();
            return DiagnosticSelection.BySeverity(all.Where(d => seen.Add(d.Message)));
        }

        /// <summary>検査し直す。前回あった指摘のうち、無くなったものの数を知らせる。</summary>
        [RelayCommand]
        public void Recheck()
        {
            var before = Rows.Select(r => r.Message).ToHashSet();
            // 直近に開いたファイルの互換の記録も「情報」として並べる (保存し直す前に確かめられるように)
            var now = Collect(_main.CurrentInputModel, _main.LastLoadCompatibility?.AsDiagnostics());
            Rows.Clear();
            foreach (var d in now) Rows.Add(new DiagnosticRow(d));

            int resolved = before.Count(m => now.All(d => d.Message != m));
            int errors = now.Count(d => d.Severity == DiagnosticSeverity.Error);
            StatusText = now.Count == 0
                ? "指摘はありません。"
                : $"指摘 {now.Count} 件 (解析を止める {errors} 件)。行を選んで「移動」で直す場所を開き、直したら「再検査」で確かめます。";
            if (resolved > 0) StatusText += $"　前回から {resolved} 件が解消しました。";
        }

        /// <summary>
        /// 選んだ指摘の場所へ移動する。関係する杭をメイン画面で選び、入力画面を開ける場所なら開く。
        /// 入力画面を閉じたら検査し直す (直した指摘が一覧から消える)。
        /// </summary>
        [RelayCommand]
        public void GoTo()
        {
            if (SelectedRow?.Diagnostic is not { } d) return;
            var selection = DiagnosticSelection.Resolve([d], _main.CurrentInputModel);
            _main.SelectForReview(selection);
            if (_main.OpenInputFor(d.Target)) Recheck();
        }
    }
}

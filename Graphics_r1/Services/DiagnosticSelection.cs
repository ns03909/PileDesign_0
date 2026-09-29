using PileDesign.Common;
using PileDesign.Models.InputData;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.Services
{
    /// <summary>問題を直しに行く入力画面。</summary>
    public enum InputDestination
    {
        /// <summary>開く画面が無い (メイン画面で杭を選ぶだけ、または解析のログで追う)。</summary>
        None,
        PileBodyWindow,
        GroundWindow,
        LoadCaseWindow,
        /// <summary>群杭沈下の入力の沈下用土層。</summary>
        SettlementLayers,
    }

    /// <summary>
    /// 問題からメイン画面で選ぶ杭・基礎梁と、その範囲の説明。
    /// <see cref="Scopes"/> は「杭 No.2 (この杭だけ)」「杭体2 を使う杭すべて: No.1・3」のように、
    /// 特定の杭なのか共有の杭体・地盤を使う杭全体なのかを書き分ける。
    /// </summary>
    public sealed record ReviewSelection(IReadOnlyList<PileLayoutDataItem> Piles, IReadOnlyList<int> BeamNos, IReadOnlyList<string> Scopes)
    {
        public static ReviewSelection Empty { get; } = new([], [], []);
        public bool IsEmpty => Piles.Count == 0 && BeamNos.Count == 0;
    }

    /// <summary>
    /// 問題 (<see cref="Diagnostic"/>) の場所から、選ぶ杭と開く入力画面を決める。文は解かない。
    ///
    /// <para>選び方の決まり:</para>
    /// <list type="bullet">
    /// <item>杭 (<see cref="DiagnosticTargetKind.Pile"/>) — その杭だけ。同じ杭体を使うほかの杭は選ばない。</item>
    /// <item>杭体・杭体の区間 — その杭体を使う杭すべて (杭体は共有なので、直すと全部に効く)。</item>
    /// <item>地盤・土層 — その地盤を使う杭すべて。</item>
    /// <item>基礎梁 — その基礎梁。</item>
    /// <item>そのほか (根入部・荷重ケース・沈下用土層・場所不明) — 杭は選ばない。</item>
    /// </list>
    /// 杭は杭配置の番号 (No) で引く。杭体の番号・地盤の番号と混ぜない。
    /// </summary>
    public static class DiagnosticSelection
    {
        /// <summary>範囲の説明に並べる杭の番号の上限。</summary>
        private const int ListedPiles = 10;

        public static ReviewSelection Resolve(IEnumerable<Diagnostic> diagnostics, InputModel? input)
        {
            var piles = new List<PileLayoutDataItem>();
            var beams = new List<int>();
            var scopes = new List<string>();
            var specific = new List<int>();
            var layout = (input?.PileLayoutItems ?? []).Where(p => p != null).ToList();

            void AddPiles(IEnumerable<PileLayoutDataItem> chosen)
            {
                foreach (var p in chosen)
                    if (!piles.Contains(p)) piles.Add(p);
            }

            void AddScope(string scope)
            {
                if (!scopes.Contains(scope)) scopes.Add(scope);
            }

            foreach (var target in (diagnostics ?? []).SelectMany(d => d.Targets).Distinct())
            {
                switch (target.Kind)
                {
                    case DiagnosticTargetKind.Pile when target.PileNo is int no:
                    {
                        var pile = layout.Where(p => p.No == no).ToList();
                        if (pile.Count == 0) break;
                        AddPiles(pile);
                        if (!specific.Contains(no)) specific.Add(no);
                        break;
                    }
                    case DiagnosticTargetKind.PileBody or DiagnosticTargetKind.PileBodySegment when target.PileBodyNo is int body:
                    {
                        var users = layout.Where(p => p.PileBodyNo == body).ToList();
                        AddPiles(users);
                        AddScope(DescribeShared($"杭体{body}", users));
                        break;
                    }
                    case DiagnosticTargetKind.Ground or DiagnosticTargetKind.GroundLayer when target.GroundNo is int ground:
                    {
                        var users = layout.Where(p => p.GroundNo == ground).ToList();
                        AddPiles(users);
                        AddScope(DescribeShared($"地盤{ground}", users));
                        break;
                    }
                    case DiagnosticTargetKind.FoundationBeam when target.BeamNo is int beam:
                        if (!beams.Contains(beam)) beams.Add(beam);
                        AddScope($"基礎梁 No.{beam}");
                        break;
                }
            }
            // 特定の杭は 1 行にまとめて先頭に置く (杭体・地盤を使う杭すべてとは別の行)
            if (specific.Count > 0)
                scopes.Insert(0, "杭 No." + string.Join("・", specific.Take(ListedPiles))
                                 + (specific.Count > ListedPiles ? $" ほか {specific.Count - ListedPiles} 本" : "")
                                 + " (その杭だけ)");
            return new ReviewSelection(piles, beams, scopes);
        }

        private static string DescribeShared(string owner, IReadOnlyList<PileLayoutDataItem> users)
        {
            if (users.Count == 0) return $"{owner} (使っている杭はありません)";
            string nos = string.Join("・", users.Take(ListedPiles).Select(p => $"No.{p.No}"));
            return $"{owner} を使う杭すべて: {nos}" + (users.Count > ListedPiles ? $" ほか {users.Count - ListedPiles} 本" : "");
        }

        /// <summary>選んだ範囲を知らせる文 (選んでいなければ null)。</summary>
        public static string? DescribeSelection(ReviewSelection selection)
        {
            if (selection.IsEmpty) return null;
            return "メイン画面で次を選択しています:\n" + string.Join("\n", selection.Scopes.Select(s => "・" + s));
        }

        /// <summary>場所から、直しに行く入力画面。</summary>
        public static InputDestination DestinationOf(DiagnosticTarget target) => target.Kind switch
        {
            DiagnosticTargetKind.PileBody or DiagnosticTargetKind.PileBodySegment => InputDestination.PileBodyWindow,
            DiagnosticTargetKind.Ground or DiagnosticTargetKind.GroundLayer => InputDestination.GroundWindow,
            DiagnosticTargetKind.LoadCase => InputDestination.LoadCaseWindow,
            DiagnosticTargetKind.SettlementLayer => InputDestination.SettlementLayers,
            _ => InputDestination.None,
        };

        /// <summary>入力画面の名前 (知らせに書く)。</summary>
        public static string DestinationName(InputDestination destination) => destination switch
        {
            InputDestination.PileBodyWindow => "杭体の入力画面",
            InputDestination.GroundWindow => "地盤の入力画面",
            InputDestination.LoadCaseWindow => "荷重条件の入力画面",
            InputDestination.SettlementLayers => "群杭沈下の沈下用土層の入力",
            _ => "",
        };

        /// <summary>推奨する操作。<see cref="Diagnostic.Remedy"/> があればそれ、無ければ場所から決める (場所不明なら null)。</summary>
        public static string? RemedyOf(Diagnostic diagnostic) => diagnostic.Remedy ?? diagnostic.Target.Kind switch
        {
            DiagnosticTargetKind.Pile => "杭配置の表で、その杭の値を直す",
            DiagnosticTargetKind.PileBody or DiagnosticTargetKind.PileBodySegment => "杭体の入力画面で直す",
            DiagnosticTargetKind.Ground or DiagnosticTargetKind.GroundLayer => "地盤の入力画面で直す",
            DiagnosticTargetKind.Embedment => "根入部の入力で直す",
            DiagnosticTargetKind.LoadCase => "荷重条件の入力画面で直す",
            DiagnosticTargetKind.FoundationBeam => "基礎梁を描き直す・つなぎ直す",
            DiagnosticTargetKind.SettlementLayer => "群杭沈下の「土層」タブで直す",
            _ => null,
        };

        /// <summary>一覧の 1 行 (【重さ】文 → 推奨する操作)。</summary>
        public static string FormatForList(Diagnostic diagnostic)
            => $"【{Diagnostic.SeverityLabel(diagnostic.Severity)}】{diagnostic.Message}"
               + (RemedyOf(diagnostic) is { } remedy ? $" → {remedy}" : "");

        /// <summary>重い順 (同じ重さの中は出た順) に並べる。</summary>
        public static List<Diagnostic> BySeverity(IEnumerable<Diagnostic> diagnostics)
            => (diagnostics ?? []).Select((d, i) => (d, i)).OrderBy(x => x.d.Severity).ThenBy(x => x.i).Select(x => x.d).ToList();

        /// <summary>対処のまとめ (推奨する操作ごとの件数)。操作が 1 つも無ければ null。</summary>
        public static string? DescribeRemedies(IEnumerable<Diagnostic> diagnostics)
        {
            var groups = (diagnostics ?? []).Select(RemedyOf).Where(r => r != null).GroupBy(r => r!).ToList();
            if (groups.Count == 0) return null;
            return "対処:\n" + string.Join("\n", groups.Select(g => $"・{g.Key} ({g.Count()} 件)"));
        }

        /// <summary>最初に直しに行く場所 (入力画面を開ける最初の問題)。無ければ null。</summary>
        public static DiagnosticTarget? FirstNavigable(IEnumerable<Diagnostic> diagnostics)
            => (diagnostics ?? []).Select(d => d.Target).FirstOrDefault(t => DestinationOf(t) != InputDestination.None);
    }
}

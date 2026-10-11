using System;
using System.Collections.Generic;
using System.Linq;

namespace PileDesign.ViewModels
{
    /// <summary>
    /// 沈下系の解析 (陳腐化の印を解析ごとに持つ単位)。
    /// </summary>
    [Flags]
    public enum SettlementAnalyses
    {
        None = 0,
        /// <summary>単杭沈下解析 (土層-杭セットごとの荷重-沈下曲線)。</summary>
        SinglePile = 1,
        /// <summary>単杭沈下解析（基礎梁考慮）。杭頭ばねに単杭沈下の曲線を使う。</summary>
        VerticalBeam = 2,
        /// <summary>群杭沈下解析（基礎梁なし）。荷重タイプ「個別十字（基礎梁反力）」は基礎梁考慮の反力を荷重に使う。</summary>
        Group = 4,
        /// <summary>群杭沈下解析（基礎梁あり）。線形ばねの反復で、ほかの解析の結果は使わない。</summary>
        GroupWithBeam = 8,
        All = SinglePile | VerticalBeam | Group | GroupWithBeam,
    }

    /// <summary>
    /// MainWindowViewModel.SettlementStaleness.cs
    ///
    /// 沈下の結果の陳腐化 (「入力変更前の結果」) を<b>解析ごとに</b>持つ。
    ///
    /// <para>以前は 4 つの解析 (単杭沈下・単杭沈下（基礎梁考慮）・群杭沈下（基礎梁なし）・群杭沈下（基礎梁あり）) で
    /// 印を 1 つ (SettlementInputChanged) だけ持ち、どれか 1 つを実行・確定すると降ろしていた。
    /// 単杭沈下と基礎梁考慮を解いたあとに軸力を変えて群杭沈下だけをやり直すと、単杭沈下と基礎梁考慮の結果まで
    /// 「最新」になり、古い曲線が黙って次の解析 (水平解析の P-S ばね・基礎梁考慮の杭頭ばね) に入った。
    /// 単杭沈下の α・n を変えて解き直しても、その曲線で解いた基礎梁考慮の結果は「最新」のままだった。</para>
    ///
    /// <para>解析の結果どうしのつながり (下流の解析は上流の結果を入力に使う):
    /// <list type="bullet">
    /// <item>単杭沈下 → 単杭沈下（基礎梁考慮）(杭頭ばねに荷重-沈下曲線)</item>
    /// <item>単杭沈下（基礎梁考慮）→ 群杭沈下（基礎梁なし）(荷重タイプ「個別十字（基礎梁反力）」の記録があるとき)</item>
    /// </list>
    /// 入力の編集は、効く解析と、その下流に印を立てる。解析を実行・確定すると<b>その解析の印だけ</b>を降ろし、
    /// 下流の解析には印を立てる (上流の結果が変わったので)。</para>
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>沈下系の解析を並び順に (表示・保存の順)。</summary>
        internal static readonly SettlementAnalyses[] EachSettlementAnalysis =
            [SettlementAnalyses.SinglePile, SettlementAnalyses.VerticalBeam, SettlementAnalyses.Group, SettlementAnalyses.GroupWithBeam];

        /// <summary>画面・メッセージに出す解析の呼び名 (README の用語)。</summary>
        internal static string SettlementAnalysisName(SettlementAnalyses kind) => kind switch
        {
            SettlementAnalyses.SinglePile => "単杭沈下解析",
            SettlementAnalyses.VerticalBeam => "単杭沈下解析（基礎梁考慮）",
            SettlementAnalyses.Group => "群杭沈下解析（基礎梁なし）",
            SettlementAnalyses.GroupWithBeam => "群杭沈下解析（基礎梁あり）",
            _ => "沈下解析",
        };

        /// <summary>解析の集まりの呼び名 (並び順)。</summary>
        internal static List<string> SettlementAnalysisNames(SettlementAnalyses set)
            => EachSettlementAnalysis.Where(k => (set & k) != 0).Select(SettlementAnalysisName).ToList();

        /// <summary>保存ファイルに書く名前 (列挙の名前。読めない名前は読込で無視する)。</summary>
        internal static List<string> SettlementAnalysisKeys(SettlementAnalyses set)
            => EachSettlementAnalysis.Where(k => (set & k) != 0).Select(k => k.ToString()).ToList();

        /// <summary>保存ファイルの名前から戻す。知らない名前は無視する。</summary>
        internal static SettlementAnalyses ParseSettlementAnalysisKeys(IEnumerable<string>? keys)
        {
            var set = SettlementAnalyses.None;
            foreach (var key in keys ?? [])
                if (Enum.TryParse<SettlementAnalyses>(key, ignoreCase: false, out var k) && EachSettlementAnalysis.Contains(k))
                    set |= k;
            return set;
        }

        // テスト (PriorityEditingFixTests ほか) がリフレクションでこの名前のフィールドを読む。名前を変えないこと
        /// <summary>入力の編集・上流の再実行で立てた印 (結果の有無に関わらず立てる。効くのは結果があるときだけ)。</summary>
        private SettlementAnalyses _staleSettlementAnalyses;

        /// <summary>
        /// 上流の解析をやり直したために立てた印。元に戻す・やり直しで入力の署名が解析時と一致しても降ろさない
        /// (入力は同じでも、使った上流の結果が変わっている)。その解析を実行したときに降ろす。
        /// </summary>
        private SettlementAnalyses _upstreamStaleSettlementAnalyses;

        /// <summary>解析ごとの、そのあとに編集した項目 (履歴の呼び名。出た順・重複なし)。</summary>
        private readonly Dictionary<SettlementAnalyses, List<string>> _editsSinceSettlementByKind =
            EachSettlementAnalysis.ToDictionary(k => k, _ => new List<string>());

        /// <summary>解析ごとの、解析したときの入力の署名 (元に戻したときに比べる相手)。分からなければ無い。</summary>
        private readonly Dictionary<SettlementAnalyses, string?> _settlementSignatureByKind = [];

        /// <summary>
        /// その解析の署名から除く名前。単杭沈下と基礎梁考慮は群杭沈下の入力 (矩形荷重・沈下用土層・荷重面) を読まない
        /// (水平解析の署名と同じ除き方)。群杭沈下は入力全体で比べる。
        /// </summary>
        private static string[]? SettlementSignatureIgnoredNames(SettlementAnalyses kind)
            => kind is SettlementAnalyses.SinglePile or SettlementAnalyses.VerticalBeam ? HorizontalSignatureIgnoredNames : null;

        private static string? SettlementSignatureOf(SettlementAnalyses kind, Models.InputData.InputModel? input)
            => InputSignature(input, alsoIgnore: SettlementSignatureIgnoredNames(kind));

        /// <summary>その解析の結果がいまあるか (印が効くのは結果があるときだけ)。</summary>
        internal bool HasSettlementResultOf(SettlementAnalyses kind)
        {
            var records = CurrentInputModel?.PileGroupSettlement?.CaseRecords;
            return kind switch
            {
                SettlementAnalyses.SinglePile => IsVerticalAnalysisDone,
                SettlementAnalyses.VerticalBeam => IsVerticalBeamAnalysisDone,
                SettlementAnalyses.Group => IsGroupPileSettlementAnalysisDone || (records?.Any(r => r != null && !r.IsBeamAware) ?? false),
                SettlementAnalyses.GroupWithBeam => records?.Any(r => r != null && r.IsBeamAware) ?? false,
                _ => false,
            };
        }

        /// <summary>
        /// 群杭沈下（基礎梁なし）の結果が、基礎梁考慮の反力を荷重に使ったものか (荷重タイプ「個別十字（基礎梁反力）」の記録がある)。
        /// </summary>
        private bool GroupUsesVerticalBeamReactions()
            => CurrentInputModel?.PileGroupSettlement?.CaseRecords?
                .Any(r => r != null && !r.IsBeamAware && r.LoadingType == "個別十字（基礎梁反力）") ?? false;

        /// <summary>その解析の結果を入力に使う解析 (直接の下流)。</summary>
        private SettlementAnalyses DirectDependentsOf(SettlementAnalyses kind) => kind switch
        {
            SettlementAnalyses.SinglePile => SettlementAnalyses.VerticalBeam,
            SettlementAnalyses.VerticalBeam => GroupUsesVerticalBeamReactions() ? SettlementAnalyses.Group : SettlementAnalyses.None,
            _ => SettlementAnalyses.None,
        };

        /// <summary>解析の集まりに、その下流 (下流の下流も) を足す。</summary>
        internal SettlementAnalyses WithSettlementDependents(SettlementAnalyses set)
        {
            var result = set;
            for (int pass = 0; pass < EachSettlementAnalysis.Length; pass++)
            {
                var next = result;
                foreach (var k in EachSettlementAnalysis)
                    if ((result & k) != 0) next |= DirectDependentsOf(k);
                if (next == result) break;
                result = next;
            }
            return result;
        }

        /// <summary>結果を持っていて、入力変更前のものになっている沈下系の解析。</summary>
        public SettlementAnalyses StaleSettlementAnalyses
        {
            get
            {
                var stale = SettlementAnalyses.None;
                foreach (var k in EachSettlementAnalysis)
                    if ((_staleSettlementAnalyses & k) != 0 && HasSettlementResultOf(k)) stale |= k;
                return stale;
            }
        }

        /// <summary>その解析 (どれか 1 つでも) の結果が入力変更前のものか。</summary>
        public bool IsSettlementResultStale(SettlementAnalyses kinds) => (StaleSettlementAnalyses & kinds) != 0;

        /// <summary>陳腐化している沈下系の解析の呼び名 (並び順)。</summary>
        internal IReadOnlyList<string> StaleSettlementAnalysisNames => SettlementAnalysisNames(StaleSettlementAnalyses);

        /// <summary>沈下解析のあとに編集した、陳腐化している解析に効く項目 (どれかの解析の分。出た順・重複なし)。</summary>
        internal IReadOnlyList<string> EditsSinceSettlement
        {
            get
            {
                var list = new List<string>();
                foreach (var k in EachSettlementAnalysis)
                    if ((_staleSettlementAnalyses & k) != 0)
                        foreach (var e in _editsSinceSettlementByKind[k])
                            if (!list.Contains(e)) list.Add(e);
                return list;
            }
        }

        /// <summary>その解析のあとに編集した項目。</summary>
        internal IReadOnlyList<string> EditsSinceSettlementOf(SettlementAnalyses kind) => _editsSinceSettlementByKind[kind];

        /// <summary>入力の編集の範囲から、印を立てる沈下系の解析を決める (下流も含む)。</summary>
        internal SettlementAnalyses SettlementAnalysesAffectedBy(AnalysisInputScope scope)
        {
            var set = SettlementAnalyses.None;
            // モデル側の入力 (地盤・杭配置・杭体・軸力・基礎梁…) は、沈下系のすべての解析に効きうる
            if (scope.HasFlag(AnalysisInputScope.Model)) set |= SettlementAnalyses.All;
            // 群杭沈下だけに効く入力 (矩形荷重・沈下用土層・荷重面・荷重面等価径)
            if (scope.HasFlag(AnalysisInputScope.Settlement)) set |= SettlementAnalyses.Group | SettlementAnalyses.GroupWithBeam;
            // 単杭沈下だけに効く入力 (単杭沈下の画面の α・n など)
            if (scope.HasFlag(AnalysisInputScope.SinglePileSettlement)) set |= SettlementAnalyses.SinglePile;
            return WithSettlementDependents(set);
        }

        /// <summary>印を立てる (下流も)。<paramref name="reason"/> は状態表示に出す「何を変えたか」。</summary>
        private void MarkSettlementStale(SettlementAnalyses kinds, string? reason)
        {
            kinds = WithSettlementDependents(kinds);
            _staleSettlementAnalyses |= kinds;
            foreach (var k in EachSettlementAnalysis)
                if ((kinds & k) != 0) AddEdit(_editsSinceSettlementByKind[k], reason);
        }

        /// <summary>印を降ろす (編集した項目の控えも消す)。</summary>
        private void ClearSettlementStale(SettlementAnalyses kinds)
        {
            _staleSettlementAnalyses &= ~kinds;
            _upstreamStaleSettlementAnalyses &= ~kinds;
            foreach (var k in EachSettlementAnalysis)
                if ((kinds & k) != 0) _editsSinceSettlementByKind[k].Clear();
        }

        /// <summary>
        /// 沈下系の解析が終わった (結果を確定した) ことを記録する。<b>その解析の印だけ</b>を降ろし、
        /// その結果を入力に使う下流の解析に印を立てる。水平解析の印には触らない。
        ///
        /// <para>以前は引数が無く、どの解析を実行しても沈下系の 4 つの印をまとめて降ろしていた。</para>
        /// </summary>
        public void MarkSettlementResultsCurrent(SettlementAnalyses kinds)
        {
            if (kinds == SettlementAnalyses.None) return;

            // 解析したときの入力 (元に戻したときに比べる相手)
            foreach (var k in EachSettlementAnalysis)
                if ((kinds & k) != 0) _settlementSignatureByKind[k] = SettlementSignatureOf(k, CurrentInputModel);
            _settlementInputSignature = InputSignature(CurrentInputModel);
            ClearSettlementStale(kinds);

            // 下流の解析は、いま変わった結果を入力に使っている。前の結果で解いたものは古い
            var downstream = SettlementAnalyses.None;
            foreach (var k in EachSettlementAnalysis)
                if ((kinds & k) != 0) downstream |= DirectDependentsOf(k);
            downstream = WithSettlementDependents(downstream) & ~kinds;
            if (downstream != SettlementAnalyses.None)
            {
                string reason = string.Join("・", EachSettlementAnalysis.Where(k => (kinds & k) != 0).Select(SettlementAnalysisName)) + "の再実行";
                MarkSettlementStale(downstream, reason);
                _upstreamStaleSettlementAnalyses |= downstream;
                if (_currentResultSet != null && IsSettlementResultStale(downstream)) InputChangedSinceAnalysis = true;
            }
            OnPropertyChanged(nameof(ResultSetStatusText));
        }

        /// <summary>
        /// 読込の仕上げ・新規作成で、沈下の印をファイルから復元した値へ戻す。
        /// <see cref="RestoreInputChangedSinceAnalysis"/> のあとに呼ぶ。
        ///
        /// <para>読込の最後の <c>SaveUndoState</c> は沈下の印も立て、変更した項目として読込の処理の名前
        /// (ApplyPostLoadProtocol) を記録する。以前は水平解析の印だけを戻していたので、沈下の結果を持つ
        /// ファイルを開いただけで「沈下解析の結果は入力変更前のもの」と出て、その名前が変更した項目として並んでいた。
        /// ファイルには変更した項目を保存していないので、項目の記録はどちらも空に戻す。</para>
        /// </summary>
        internal void RestoreSettlementStale(SettlementAnalyses stale)
        {
            _editsSinceHorizontal.Clear();
            ClearSettlementStale(SettlementAnalyses.All);   // 項目の記録も空になる
            _staleSettlementAnalyses = stale;
            if (SettlementResultsAreStale) InputChangedSinceAnalysis = true;
            OnPropertyChanged(nameof(ResultSetStatusText));
        }

        /// <summary>沈下の印の控え (読込に失敗したときに戻す)。</summary>
        private sealed record SettlementStaleSnapshot(
            SettlementAnalyses Stale, SettlementAnalyses Upstream,
            Dictionary<SettlementAnalyses, List<string>> Edits, Dictionary<SettlementAnalyses, string?> Signatures, string? LatestSignature);

        private SettlementStaleSnapshot CaptureSettlementStale() => new(
            _staleSettlementAnalyses, _upstreamStaleSettlementAnalyses,
            _editsSinceSettlementByKind.ToDictionary(p => p.Key, p => p.Value.ToList()),
            new Dictionary<SettlementAnalyses, string?>(_settlementSignatureByKind), _settlementInputSignature);

        private void RestoreSettlementStale(SettlementStaleSnapshot s)
        {
            _staleSettlementAnalyses = s.Stale;
            _upstreamStaleSettlementAnalyses = s.Upstream;
            foreach (var k in EachSettlementAnalysis)
            {
                _editsSinceSettlementByKind[k].Clear();
                if (s.Edits.TryGetValue(k, out var e)) _editsSinceSettlementByKind[k].AddRange(e);
            }
            _settlementSignatureByKind.Clear();
            foreach (var p in s.Signatures) _settlementSignatureByKind[p.Key] = p.Value;
            _settlementInputSignature = s.LatestSignature;
        }

        /// <summary>
        /// 元に戻す・やり直しのあとに、沈下系の解析ごとに、解析したときの入力の署名と今の入力の署名を比べて印を決め直す。
        /// 署名の分からない解析・結果の無い解析は今の印のまま。上流をやり直したための印は降ろさない。
        /// 署名を作れなかったら false (印は変えない)。
        /// </summary>
        private bool RecheckSettlementAgainstAnalysis()
        {
            var computed = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var k in EachSettlementAnalysis)
            {
                if (!_settlementSignatureByKind.TryGetValue(k, out var recorded) || recorded == null) continue;
                if (!HasSettlementResultOf(k)) continue;
                string cacheKey = string.Join(",", SettlementSignatureIgnoredNames(k) ?? []);
                if (!computed.TryGetValue(cacheKey, out var current))
                    computed[cacheKey] = current = SettlementSignatureOf(k, CurrentInputModel);
                if (current == null) return false;

                bool stale = current != recorded || (_upstreamStaleSettlementAnalyses & k) != 0;
                if (stale) _staleSettlementAnalyses |= k;
                else
                {
                    _staleSettlementAnalyses &= ~k;
                    _editsSinceSettlementByKind[k].Clear();
                }
            }
            return true;
        }

        /// <summary>署名と結果のそろった沈下系の解析が 1 つでもあるか (印を決め直せるか)。</summary>
        private bool CanRecheckSettlement()
            => EachSettlementAnalysis.Any(k => _settlementSignatureByKind.TryGetValue(k, out var s) && s != null && HasSettlementResultOf(k));

        /// <summary>読込の仕上げで、沈下系の解析ごとの署名を取る (印の立っていない解析だけ。今の入力が解析時の入力)。</summary>
        private void CaptureSettlementSignaturesAfterLoad(bool inputIsTheAnalyzedOne)
        {
            _settlementSignatureByKind.Clear();
            if (!inputIsTheAnalyzedOne) { _settlementInputSignature = null; return; }
            foreach (var k in EachSettlementAnalysis)
                if ((_staleSettlementAnalyses & k) == 0)
                    _settlementSignatureByKind[k] = SettlementSignatureOf(k, CurrentInputModel);
            _settlementInputSignature = SettlementResultsAreStale ? null : InputSignature(CurrentInputModel);
        }
    }
}

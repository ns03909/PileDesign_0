using PileDesign.FEM;
using PileDesign.Models.InputData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PileDesign.Services
{
    /// <summary>
    /// 単杭沈下の画面の 1 回の編集 (開いてから OK / キャンセルまで) の解析と結果の管理。画面を持たない。
    ///
    /// <list type="bullet">
    /// <item>解析の結果は<b>画面の写しの土層-杭セット</b>と、そのセットを使う杭の単杭沈下量へ書く。
    ///   元の入力の土層-杭セットへは書かず、OK のときに写しをまとめて反映する (<see cref="CommitCopies"/>)。</item>
    /// <item>杭体の入力と各杭の単杭沈下量は入力の実体へ書くので、開いた時点で控え、キャンセルで戻す (<see cref="Cancel"/>)。</item>
    /// <item>非同期の解析は対象を開始時点で固定し、同じセットで後から始めた解析があれば古い結果を捨てる。
    ///   閉じたあと (<see cref="IsClosed"/>) は何も反映しない (<see cref="RunAsync"/>)。</item>
    /// </list>
    ///
    /// 以前はこれらが SettlementViewModel にあり、画面なしで確かめられなかった (2026-10-10 外部レビューの保守性の提案)。
    /// </summary>
    internal sealed class SingleSettlementSession
    {
        private readonly InputModel _input;
        private readonly Func<IList<SoilPile>> _workingSets;

        // 開いた時点の杭体の入力と、各杭の単杭沈下の結果の控え (キャンセルで戻す)
        private readonly List<PileBodyInput?> _bodiesAtOpen;
        private readonly List<(PileLayoutDataItem Pile, PileLayoutDataItem.SinglePileSettlementState State)> _settlementsAtOpen;

        // 非同期の解析の世代 (土層-杭セットごと)
        private readonly Dictionary<SoilPile, int> _generations = new(ReferenceEqualityComparer.Instance);

        // 結果が今の入力に追いついていないセット (解き直しを始めた・入力の検査で止まった・解析に失敗した)。
        // 解析が成功して結果を書いたら外す。OK はこれが空のときだけ確定する (<see cref="StaleSetNumbers"/>)
        private readonly HashSet<SoilPile> _stale = new(ReferenceEqualityComparer.Instance);

        /// <param name="input">編集する入力 (杭体・杭配置は実体を編集する)。</param>
        /// <param name="workingSets">画面の写しの土層-杭セットの一覧 (画面の元に戻す操作で差し替わるので、その時点の一覧を返す)。</param>
        public SingleSettlementSession(InputModel input, Func<IList<SoilPile>> workingSets)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _workingSets = workingSets ?? throw new ArgumentNullException(nameof(workingSets));
            _bodiesAtOpen = (input.PileBodies ?? []).Select(pb => pb?.DeepCopy()).ToList();
            _settlementsAtOpen = (input.PileLayoutItems ?? []).Where(p => p != null)
                .Select(p => (p, p.CaptureSinglePileSettlement())).ToList();
        }

        /// <summary>開いた時点の杭体の入力の控え (画面の元に戻す操作の最初の状態にも使う)。</summary>
        public IReadOnlyList<PileBodyInput?> BodiesAtOpen => _bodiesAtOpen;

        /// <summary>OK かキャンセルで閉じたか。閉じたあとは解析の結果を反映しない。</summary>
        public bool IsClosed { get; private set; }

        /// <summary>
        /// セットの結果が今の入力に追いついていない印を付ける。入力を変えて解き直すとき、<b>解き始める前</b>
        /// (入力の検査より前) に呼ぶ。検査で止まった・解析に失敗した・まだ終わっていないセットは印が残る。
        /// </summary>
        public void MarkStale(SoilPile soilPile) => _stale.Add(soilPile);

        /// <summary>
        /// 結果が今の入力に追いついていないセットの番号 (一覧の位置 + 1、昇順)。空でなければ OK で確定しない
        /// (以前は解析中・失敗のあとでも OK で古い曲線を確定し、画面は結果を「最新」と記録していた)。
        /// 元に戻す操作で一覧が差し替わったときは、今の一覧にあるセットだけを数える。
        /// </summary>
        public List<int> StaleSetNumbers()
        {
            var sets = _workingSets();
            return [.. Enumerable.Range(0, sets.Count).Where(i => sets[i] != null && _stale.Contains(sets[i])).Select(i => i + 1)];
        }

        /// <summary>1 つの土層-杭セットを解く (同期)。</summary>
        public VerticalLoadTransferMethod Solve(SoilPile soilPile) => new(_input, soilPile);

        /// <summary>位置 <paramref name="index"/> のセットを解いて結果を書く (一括解析・同じ杭体のセットの解き直し)。</summary>
        public void AnalyzeAt(int index, List<string> warnings)
        {
            var soilPile = _workingSets()[index];
            Apply(soilPile, Solve(soilPile), warnings);
        }

        /// <summary>
        /// 1 つの土層-杭セットの解析結果を、写しのセットと、そのセットを使う杭の単杭沈下量へ書く。
        /// <b>元の入力の土層-杭セットへは書かない。</b>
        /// </summary>
        public void Apply(SoilPile soilPile, VerticalLoadTransferMethod vtm, List<string> warnings)
        {
            _stale.Remove(soilPile);
            soilPile.LoadDisplacements = vtm.LoadDisplacements;
            soilPile.LoadDisplacementsLimit = vtm.LoadDisplacementsLimit;
            soilPile.NodeDisplacements = vtm.Ds;
            soilPile.NodeReactions = vtm.Rs;

            // 該当するPileLayoutItemの沈下量を計算 (セットの番号は一覧の位置 + 1)
            int soilPileNo = _workingSets().IndexOf(soilPile) + 1;
            if (soilPileNo <= 0) return;
            foreach (var pile in _input.PileLayoutItems ?? [])
            {
                if (pile.SoilPileAltNo == soilPileNo)
                    WriteSinglePileSettlements(_input, pile, vtm);
            }
            // 曲線の精度が落ちた/範囲外の軸力があった杭を集める (沈下量の取り出しの後で見る)
            foreach (var w in vtm.Warnings)
                warnings.Add($"杭体 {soilPileNo}: {w}");
        }

        /// <summary>
        /// セットを非同期に解き、結果を書く。対象は開始時点で固定する。同じセットで後から始めた解析があったとき・閉じたあとは
        /// 反映せず null を返す (完了の順が逆転しても古い結果で上書きしない)。
        /// </summary>
        /// <param name="runner">解析の代わり (テスト用)。null なら荷重伝達法を Task.Run で解く。</param>
        public async Task<(VerticalLoadTransferMethod Result, List<string> Warnings)?> RunAsync(
            SoilPile soilPile, Func<InputModel, SoilPile, Task<VerticalLoadTransferMethod>>? runner)
        {
            int generation = _generations.TryGetValue(soilPile, out int g) ? g + 1 : 1;
            _generations[soilPile] = generation;

            var input = _input;
            var vtm = runner != null
                ? await runner(input, soilPile)
                : await Task.Run(() => new VerticalLoadTransferMethod(input, soilPile));

            if (IsClosed || _generations[soilPile] != generation) return null;

            var warnings = new List<string>();
            Apply(soilPile, vtm, warnings);
            return (vtm, warnings);
        }

        /// <summary>OK: 閉じて、写しの土層-杭セットを元の入力へまとめて反映する一覧を返す。</summary>
        public ObservableCollection<SoilPile> CommitCopies()
        {
            IsClosed = true;
            return new ObservableCollection<SoilPile>(_workingSets().Select(pile => pile.DeepCopy()));
        }

        /// <summary>キャンセル: 閉じて、各杭の単杭沈下の結果と杭体の入力を開いた時点へ戻す。写しのセットは捨てるだけ。</summary>
        public void Cancel()
        {
            IsClosed = true;
            foreach (var (pile, state) in _settlementsAtOpen)
                pile.RestoreSinglePileSettlement(state);

            // 杭体入力は実体を編集しているため、開いた時点の値へ戻す。
            // 戻す項目は並べない (PileBodyInput.RestoreScalarsFrom の説明を参照)。
            var bodies = _input.PileBodies;
            if (bodies != null && bodies.Count == _bodiesAtOpen.Count)
            {
                for (int i = 0; i < bodies.Count; i++)
                {
                    if (_bodiesAtOpen[i] is { } before) bodies[i]?.RestoreScalarsFrom(before);
                }
            }
        }

        /// <summary>
        /// 杭の単杭沈下量 (常時・レベル1・レベル2) を荷重-沈下曲線から求めて杭に書き、曲線の範囲外だったかも残す。
        /// 求めた (軸力, 沈下量 [m]) を返す (グラフの点)。引けなかった値は書かない (前回値が残る。理由は Warnings)。
        ///
        /// <para>以前は解析後の書き込みとグラフの更新に同じ処理が 2 つあった (写し)。範囲外の印を片方にだけ
        /// 足すと取り残されるので 1 つにした。</para>
        /// </summary>
        public static (List<(double Force, double Settlement)> VL, List<(double Force, double Settlement)> Level1,
            List<(double Force, double Settlement)> Level2) WriteSinglePileSettlements(InputModel? input, PileLayoutDataItem pile, VerticalLoadTransferMethod vtm)
        {
            var vl = new List<(double, double)>();
            var l1 = new List<(double, double)>();
            var l2 = new List<(double, double)>();

            double force = pile.AxialForceVL0 + pile.AxialForceVLAdditional;
            var v = vtm.GetDisplacementForGivenLoad(force, out bool beyond);
            if (v != null)
            {
                pile.SinglePileSettlementVL = v[0];
                pile.SinglePileSettlementVLBeyondCurve = beyond;
                vl.Add((force, v[0]));
            }

            // 地震時軸力の分離入力では、各ケースの組合せのうち圧縮が最大の軸力 (SeismicAxialForces.PerCaseForSettlement)
            pile.SinglePileSettlementLevel1sBeyondCurve = LevelSettlements(SeismicAxialForces.PerCaseForSettlement(input, pile, 1), pile.SinglePileSettlementLevel1s,
                pile.SinglePileSettlementLevel1sBeyondCurve, l1);
            // 分離入力ではケースごとの欄には組合せのうち軸力が最大のものが入る。グラフの点は下で組合せごとに足すので、ここでは集めない
            bool separated = SeismicAxialForces.IsSeparated(input);
            pile.SinglePileSettlementLevel2sBeyondCurve = LevelSettlements(SeismicAxialForces.PerCaseForSettlement(input, pile, 2), pile.SinglePileSettlementLevel2s,
                pile.SinglePileSettlementLevel2sBeyondCurve, separated ? new List<(double, double)>() : l2);
            if (separated) l1.Clear();

            // 地震時軸力の分離入力: (レベル, ケース, 組合せ) ごとに求めて残す (βU・βL の同じ組合せは 1 件)。
            // 引けなかったものは残さない (表・図は「結果なし」になる。前回値を黙って使わない)
            var separatedResults = new ObservableCollection<SeparatedSettlementResult>();
            if (separated)
            {
                var combinations = SeismicAxialForces.CombinationsForVerticalAnalysis(input);
                foreach (int level in new[] { 1, 2 })
                {
                    int count = pile.AxialForceUpperOf(level)?.Count ?? 0;
                    for (int no = 1; no <= count; no++)
                        foreach (var c in combinations)
                        {
                            if (c == null) continue;
                            double n = SeismicAxialForces.ForVerticalAnalysis(input, pile, no, level, c);
                            var sv = vtm.GetDisplacementForGivenLoad(n, out bool b);
                            if (sv == null) continue;
                            separatedResults.Add(new SeparatedSettlementResult
                            {
                                Level = level, CaseNo = no, CombinationNo = c.No, BetaU = c.Beta1, BetaL = c.Beta2,
                                AxialLoad = n, Settlement = sv[0], BeyondCurve = b,
                            });
                            (level == 1 ? l1 : l2).Add((n, sv[0]));
                        }
                }
            }
            pile.SinglePileSettlementsSeparated = separatedResults;
            return (vl, l1, l2);

            ObservableCollection<bool> LevelSettlements(
                IReadOnlyList<double> forces, IList<double> settlements, IList<bool>? oldFlags, List<(double, double)> points)
            {
                var flags = new ObservableCollection<bool>(
                    Enumerable.Range(0, forces.Count).Select(j => PileLayoutDataItem.BeyondAt(oldFlags, j)));
                for (int j = 0; j < forces.Count; j++)
                {
                    var sv = vtm.GetDisplacementForGivenLoad(forces[j], out bool b);
                    if (sv == null || j >= settlements.Count) continue;
                    settlements[j] = sv[0];
                    flags[j] = b;
                    points.Add((forces[j], sv[0]));
                }
                return flags;
            }
        }
    }
}

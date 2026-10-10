using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PileDesign.FEM
{
    /// <summary>
    /// SoilPile.LoadDisplacementsの(DD0s, PileTopLoad)曲線を補間し、
    /// 鉛直杭ばねの接線/割線剛性を返す。
    /// 符号規約: settlement_m > 0 = 沈下（下向き）
    /// FEM側: Uz < 0 = 沈下 → settlement_m = -Uz
    /// </summary>
    public class VerticalPileSpringCurve
    {
        // (沈下量[m], 杭頭荷重[kN]) のソート済みリスト（沈下正=下向き、荷重正=圧縮）
        private readonly List<(double Settlement_m, double Force_kN)> _curve;

        // 初期接線剛性（最初の区間の勾配）
        public double InitialTangentStiffness { get; private set; }

        // 最小剛性フロア値（初期剛性の1%）
        private double _minStiffness;

        // 引抜き側 (浮き上がり) の曲線: (浮き上がり量 [m, 正], 引抜き抵抗 [kN, 正]) のソート済みリスト。
        // 単杭沈下解析の荷重～沈下量関係 (LoadDisplacements) から作るときだけ持つ (引抜き側の点が無ければ空)。
        // null は「引抜き側を持たない」作り方 (節点別の点列。水平解析が使う) で、従来どおり押込みの初期剛性を返す。
        private readonly List<(double Uplift_m, double Force_kN)>? _tension;

        // 引抜き側の最小剛性 (引抜き側の初期剛性の 1%、引抜き抵抗が無ければ 1 kN/m)
        private double _minTensionStiffness = 1.0;

        /// <summary>引抜き側の初期接線剛性 [kN/m]。引抜き側の曲線が無ければ 0。</summary>
        public double InitialTensionStiffness { get; private set; }

        public VerticalPileSpringCurve(
            ObservableCollection<VerticalLoadTransferMethod.LoadDisplacement> loadDisplacements)
        {
            if (loadDisplacements == null || loadDisplacements.Count == 0)
                throw new ArgumentException("LoadDisplacementsが空です。沈下解析を先に実行してください。");

            // DD0s(mm) → m に変換し、PileTopLoadとペアでリスト化
            // DD0sが正=沈下のデータのみ使用（圧縮側）
            _curve = loadDisplacements
                .Where(ld => ld.DD0s >= 0)
                .OrderBy(ld => ld.DD0s)
                .Select(ld => (Settlement_m: ld.DD0s / 1000.0, Force_kN: ld.PileTopLoad))
                .ToList();

            // 引抜き側 (DD0s < 0・杭頭荷重が引張) も同じ解析で求めてある。浮き上がる杭はこちらの剛性で抵抗する。
            // 以前は押込み側だけを使い、浮き上がると押込みの初期剛性で抵抗させたうえ、報告する反力は 0 だった
            _tension = loadDisplacements
                .Where(ld => ld.DD0s < 0 && double.IsFinite(ld.DD0s) && double.IsFinite(ld.PileTopLoad))
                .Select(ld => (Uplift_m: -ld.DD0s / 1000.0, Force_kN: Math.Max(-ld.PileTopLoad, 0.0)))
                .OrderBy(p => p.Uplift_m)
                .ToList();

            FinalizeCurveSetup();
            FinalizeTensionSetup();
        }

        private void FinalizeTensionSetup()
        {
            if (_tension == null) return;
            _tension.Insert(0, (0.0, 0.0));
            for (int i = _tension.Count - 1; i > 0; i--)
                if (_tension[i].Uplift_m - _tension[i - 1].Uplift_m < 1e-15)
                    _tension.RemoveAt(i);
            InitialTensionStiffness = _tension.Count >= 2 && _tension[1].Uplift_m > 1e-15
                ? Math.Max(_tension[1].Force_kN / _tension[1].Uplift_m, 0.0)
                : 0.0;
            _minTensionStiffness = Math.Max(InitialTensionStiffness * 0.01, 1.0);
        }

        /// <summary>浮き上がり量 [m, 正] での引抜き抵抗 [kN, 正]。曲線の端より先は端の値 (引抜き抵抗の限界) で一定。</summary>
        private double TensionForce(double uplift_m)
        {
            var t = _tension!;
            if (t.Count < 2) return 0.0;
            for (int i = 0; i < t.Count - 1; i++)
            {
                if (uplift_m <= t[i + 1].Uplift_m)
                {
                    double dD = t[i + 1].Uplift_m - t[i].Uplift_m;
                    double ratio = dD > 1e-15 ? (uplift_m - t[i].Uplift_m) / dD : 1.0;
                    return t[i].Force_kN + ratio * (t[i + 1].Force_kN - t[i].Force_kN);
                }
            }
            return t[^1].Force_kN;
        }

        /// <summary>浮き上がり量 [m, 正] での引抜き側の接線剛性 [kN/m]。</summary>
        private double TensionTangent(double uplift_m)
        {
            var t = _tension!;
            for (int i = 0; i < t.Count - 1; i++)
            {
                if (uplift_m <= t[i + 1].Uplift_m)
                {
                    double dD = t[i + 1].Uplift_m - t[i].Uplift_m;
                    if (dD < 1e-15) return _minTensionStiffness;
                    return Math.Max((t[i + 1].Force_kN - t[i].Force_kN) / dD, _minTensionStiffness);
                }
            }
            return _minTensionStiffness;   // 引抜き抵抗の限界を超えた
        }

        /// <summary>
        /// 節点別 (相対変位 m, 反力 kN) のペアリストから構築するコンストラクタ。
        /// 杭周面摩擦 / 杭先端反力の深度分布を各節点ごとに個別の非線形ばねに変換する用途。
        /// </summary>
        public VerticalPileSpringCurve(IEnumerable<(double Settlement_m, double Force_kN)> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            _curve = points
                .Where(p => p.Settlement_m >= 0)
                .OrderBy(p => p.Settlement_m)
                .ToList();
            if (_curve.Count == 0)
                throw new ArgumentException("点列が空 (圧縮側データなし) です。", nameof(points));

            FinalizeCurveSetup();
        }

        private void FinalizeCurveSetup()
        {
            // 0点が無い場合は追加
            if (_curve.Count == 0 || _curve[0].Settlement_m > 1e-12)
            {
                _curve.Insert(0, (0.0, 0.0));
            }

            // 重複変位の除去 (補間時のゼロ割回避)
            for (int i = _curve.Count - 1; i > 0; i--)
            {
                if (_curve[i].Settlement_m - _curve[i - 1].Settlement_m < 1e-15)
                    _curve.RemoveAt(i);
            }

            // 初期接線剛性の計算
            if (_curve.Count >= 2)
            {
                double dD = _curve[1].Settlement_m - _curve[0].Settlement_m;
                double dF = _curve[1].Force_kN - _curve[0].Force_kN;
                InitialTangentStiffness = dD > 1e-15 ? dF / dD : FemConstants.DefaultVerticalPileStiffness; // kN/m
            }
            else
            {
                InitialTangentStiffness = FemConstants.DefaultVerticalPileStiffness; // デフォルト
            }

            // 初期接線が負/0 のときも下限値で救う (節点別カーブで反力ほぼゼロのケース対応)
            if (InitialTangentStiffness <= 0)
                InitialTangentStiffness = FemConstants.DefaultVerticalPileStiffness;

            _minStiffness = Math.Max(InitialTangentStiffness * 0.01, 1.0); // 最低1 kN/m
        }

        /// <summary>
        /// 指定沈下量における杭頭荷重を線形補間で返す
        /// </summary>
        /// <param name="settlement_m">沈下量 [m]（正=下向き）</param>
        /// <returns>杭頭荷重 [kN]（正=圧縮）</returns>
        public double GetForce(double settlement_m)
        {
            // 浮き上がり: 引抜き側の曲線 (引張なので負)。引抜き側を持たない作り方は従来どおり 0
            if (settlement_m < 0 && _tension != null)
                return -Math.Max(TensionForce(-settlement_m), _minTensionStiffness * -settlement_m);
            if (settlement_m <= 0) return 0.0;
            if (_curve.Count < 2) return 0.0;

            // 曲線範囲内を検索
            for (int i = 0; i < _curve.Count - 1; i++)
            {
                if (settlement_m <= _curve[i + 1].Settlement_m)
                {
                    double dD = _curve[i + 1].Settlement_m - _curve[i].Settlement_m;
                    if (dD < 1e-15) return _curve[i].Force_kN;
                    double ratio = (settlement_m - _curve[i].Settlement_m) / dD;
                    return _curve[i].Force_kN + ratio * (_curve[i + 1].Force_kN - _curve[i].Force_kN);
                }
            }

            // 曲線範囲外: 最後の区間の勾配で外挿
            var last = _curve[^1];
            var prev = _curve[^2];
            double slope = (last.Settlement_m - prev.Settlement_m) > 1e-15
                ? (last.Force_kN - prev.Force_kN) / (last.Settlement_m - prev.Settlement_m)
                : 0.0;
            return last.Force_kN + slope * (settlement_m - last.Settlement_m);
        }

        /// <summary>
        /// 指定沈下量における接線剛性 dF/dD [kN/m] を返す
        /// </summary>
        public double GetTangentStiffness(double settlement_m)
        {
            if (settlement_m < 0 && _tension != null) return TensionTangent(-settlement_m);
            if (settlement_m <= 0) return InitialTangentStiffness;
            if (_curve.Count < 2) return InitialTangentStiffness;

            // 該当区間を検索
            for (int i = 0; i < _curve.Count - 1; i++)
            {
                if (settlement_m <= _curve[i + 1].Settlement_m)
                {
                    double dD = _curve[i + 1].Settlement_m - _curve[i].Settlement_m;
                    if (dD < 1e-15) return _minStiffness;
                    double k = (_curve[i + 1].Force_kN - _curve[i].Force_kN) / dD;
                    return Math.Max(k, _minStiffness);
                }
            }

            // 曲線範囲外: 最終区間の勾配（フロア値適用）
            var last = _curve[^1];
            var prev = _curve[^2];
            double dDlast = last.Settlement_m - prev.Settlement_m;
            if (dDlast < 1e-15) return _minStiffness;
            double kLast = (last.Force_kN - prev.Force_kN) / dDlast;
            return Math.Max(kLast, _minStiffness);
        }

        /// <summary>
        /// 指定沈下量における割線剛性 F/D [kN/m] を返す
        /// </summary>
        public double GetSecantStiffness(double settlement_m)
        {
            // 浮き上がり: 引抜き側の割線剛性。要素の内力 (割線 × 変位) と報告する反力 (GetForce) が一致する
            if (settlement_m < -1e-12 && _tension != null)
                return -GetForce(settlement_m) / -settlement_m;
            if (settlement_m <= 1e-12) return InitialTangentStiffness;

            double force = GetForce(settlement_m);
            double k = force / settlement_m;
            return Math.Max(k, _minStiffness);
        }
    }
}

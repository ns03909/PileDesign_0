
using MathNet.Numerics.LinearAlgebra;
using PileDesign.Models.InputData;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

using Serilog;
namespace PileDesign.FEM
{
    public class Beam
    {
        public string Name { get; set; }
        public Section Section { get; set; }
        public Node NodeI { get; set; }
        public Node NodeJ { get; set; }
        public double Length { get; set; }
        /// <summary>
        /// 杭頭部ゾーン判定（場所打ち鋼管コンクリート杭の鋼管コンクリート部において、
        /// 杭頭から 0.5D 以内にある要素で true）。M-φ の杭頭/杭中間カーブ切替に使用。
        /// 非鋼管コンクリート部の杭では全要素が true になる（pileTopZoneBottom = -∞）ため
        /// 「各杭の最上段要素」判定には使用しないこと — その用途には <see cref="IsPileHeadElement"/> を使う。
        /// </summary>
        public bool IsPileTop { get; set; } = false;

        /// <summary>
        /// 各杭の最上段（I 端が杭頭側）要素で true。SegmentIndex == 0 と等価。
        /// 「杭頭」値表示・杭頭応力テーブル等の「topmost element per pile」用途で使用。
        /// </summary>
        public bool IsPileHeadElement { get; set; } = false;

        // どの杭体/どのセグメントか（Solver が PileSection に辿るためのメタ情報）
        public int? PileBodyNo { get; set; }
        public int? SegmentIndex { get; set; }

        // M-φ 非線形（N依存の曲線ファミリを保持：合成 or 個別）
        // 例: family の各要素 { double N; IEnumerable<(double Phi,double Moment)> Points; } など
        public object? MPhi_ByN { get; set; }   // 合成: Mres-φres 用
        public object? MphiY_ByN { get; set; }  // 個別: My-φy
        public object? MphiZ_ByN { get; set; }  // 個別: Mz-φz

        // 解決後（N固定）の曲線をキャッシュ
        private MomentCurvatureCurve? _combinedCurve;
        // 注: 個別 M-φ 曲線 (_yCurve / _zCurve) は将来の拡張用として保留中。
        // 現状は合成曲線 (_combinedCurve) のみ使用する。

        // 曲線の初期接線剛性（φ→0 での dM/dφ）：EI正規化の基準
        public double InitialCurveTangent { get; private set; }

        // 解決済みの合成M-φ曲線を取得（グラフ表示用）
        public MomentCurvatureCurve? ResolvedCombinedCurve => _combinedCurve;

        // 追加: PileSection.GetMPhiRelationship で得た曲線（合成）を直接設定
        // 注: 入力はFEM解析の単位系（φ: 1/m, M: kN·m）を期待
        //     PHC/PRC/SC杭のGetMPhiRelationshipは既にFEM単位で値を返すため、単位変換は不要
        public void SetResolvedCombinedMPhi(System.Collections.Generic.IEnumerable<double> phis,
                                            System.Collections.Generic.IEnumerable<double> moments)
        {
            // 入力を検査・正規化して不正値や重複phiを除去/集約する
            if (phis == null || moments == null) { _combinedCurve = null; return; }

            var pList = phis.ToList();
            var mList = moments.ToList();
            if (pList.Count != mList.Count || pList.Count < 2) { _combinedCurve = null; return; }

            // デバッグ: 入力値を出力（単位変換なし：入力はFEM単位系を期待）
            // System.Diagnostics.Debug.WriteLine($"[DEBUG] SetResolvedCombinedMPhi: Beam={Name}, Points={pList.Count}, " +
            //     $"phi[0]={pList[0]:E6} [1/m], phi[last]={pList[^1]:E6} [1/m], M[0]={mList[0]:F1} [kNm], M[last]={mList[^1]:F1} [kNm]");

            // フィルタ: 有限値のみ
            var pairs = new System.Collections.Generic.List<(double Phi, double Moment)>(pList.Count);
            for (int i = 0; i < pList.Count; i++)
            {
                double p = pList[i], m = mList[i];
                if (!double.IsFinite(p) || !double.IsFinite(m)) continue;
                pairs.Add((p, m));
            }

            if (pairs.Count < 2) { _combinedCurve = null; return; }

            // ソートして近傍phiをマージ（平均化）
            pairs = pairs.OrderBy(t => t.Phi).ToList();
            const double EPS_PHI = 1e-12;
            var merged = new System.Collections.Generic.List<(double Phi, double Moment)>(pairs.Count);

            double curP = pairs[0].Phi;
            double curM = pairs[0].Moment;
            int cnt = 1;
            for (int i = 1; i < pairs.Count; i++)
            {
                var (p, m) = pairs[i];
                if (Math.Abs(p - curP) <= EPS_PHI)
                {
                    curM += m;
                    cnt++;
                }
                else
                {
                    merged.Add((curP, curM / cnt));
                    curP = p;
                    curM = m;
                    cnt = 1;
                }
            }
            merged.Add((curP, curM / cnt));

            if (merged.Count < 2) { _combinedCurve = null; return; }

            // MomentCurvatureCurve 側へ渡す前に double.NaN 等が混入していないか二重チェック
            var cleanPts = merged.Where(t => double.IsFinite(t.Phi) && double.IsFinite(t.Moment)).ToList();
            if (cleanPts.Count < 2) { _combinedCurve = null; return; }

            // 曲線作成（安全化済みデータ）
            _combinedCurve = new MomentCurvatureCurve(cleanPts.Select(t => (t.Phi, t.Moment)));

            // 曲線の初期接線剛性を計算（φ→0 での dM/dφ）
            // 最初の2点間の傾きを使用
            if (cleanPts.Count >= 2)
            {
                var p0 = cleanPts[0];
                var p1 = cleanPts[1];
                double dPhi = p1.Phi - p0.Phi;
                InitialCurveTangent = (dPhi > 1e-15) ? (p1.Moment - p0.Moment) / dPhi : 0.0;
            }
            else
            {
                InitialCurveTangent = 0.0;
            }

            // デバッグログ（先頭/末尾の点を出力）
            try
            {
                var first = cleanPts.First();
                var last = cleanPts.Last();
                // System.Diagnostics.Debug.WriteLine($"[v2] SetResolvedCombinedMPhi: Beam={Name}, Points={cleanPts.Count}, first={first.Phi:E6}/{first.Moment:E6}, last={last.Phi:E6}/{last.Moment:E6}, InitialCurveTangent={InitialCurveTangent:E6}");
            }
            catch (Exception ex) { Log.Warning(ex, "SetResolvedCombinedMPhi"); }
        }

        // 荷重ケースの代表軸力 N を与えて、そのケース用の曲線を解決（線形補間）
        public void ResolveMPhiForAxial(double axialN)
        {
            // 旧: _combinedCurve = (MPhi_ByN != null) ? AxialCurveFamily.ResolveMPhi(MPhi_ByN, axialN) : null;
            if (MPhi_ByN == null)
            {
                _combinedCurve = null;
                // System.Diagnostics.Debug.WriteLine($"ResolveMPhiForAxial: Beam={Name}, MPhi_ByN=null");
                return;
            }

            try
            {
                var resolved = AxialCurveFamily.ResolveMPhi(MPhi_ByN, axialN);
                _combinedCurve = resolved;
                if (resolved == null)
                {
                    // System.Diagnostics.Debug.WriteLine($"ResolveMPhiForAxial: Beam={Name}, resolved=null for N={axialN:E}");
                }
                else
                {
                    // 可能なら点数と端点を出力（MomentCurvatureCurve が Points を公開している場合）
                    try
                    {
                        var ptsField = resolved.GetType().GetProperty("Points");
                        if (ptsField != null)
                        {
                            var pts = ptsField.GetValue(resolved) as System.Collections.IEnumerable;
                            if (pts != null)
                            {
                                var list = pts.Cast<object>().Select(o =>
                                {
                                    var pi = o.GetType().GetProperty("Phi")?.GetValue(o);
                                    var mi = o.GetType().GetProperty("Moment")?.GetValue(o);
                                    return (Phi: Convert.ToDouble(pi), Moment: Convert.ToDouble(mi));
                                }).ToList();
                                int cnt = list.Count;
                                var first5 = string.Join(", ", list.Take(5).Select(t => $"{t.Phi:E6}/{t.Moment:E6}"));
                                var last = list.Last();
                                // System.Diagnostics.Debug.WriteLine($"ResolveMPhiForAxial: Beam={Name}, Points={cnt}, first5=[{first5}], last={last.Phi:E6}/{last.Moment:E6}");
                            }
                        }
                    }
                    catch { /* best-effort logging */ }
                }
            }
            catch (Exception ex)
            {
                _combinedCurve = null;
                // System.Diagnostics.Debug.WriteLine($"ResolveMPhiForAxial: Beam={Name}, Exception resolving for N={axialN:E}: {ex}");
            }
        }

        // 要素中央曲率から接線剛性（EI_eff）を返す
        // 合成: φres=√(φy^2+φz^2) → dM/dφ(φres) を両軸に適用
        // 個別: φy→dMy/dφy, φz→dMz/dφz
        public (double EIy_eff, double EIz_eff) EvaluateEIeff(double phiY, double phiZ)
        {
            // 初期 EI を取得（null 安全）
            double EI0y = 0.0;
            double EI0z = 0.0;
            try
            {
                if (Section?.Material != null)
                {
                    EI0y = Section.Material.E * Section.IY;
                    EI0z = Section.Material.E * Section.IZ;
                }
            }
            catch { /* 無視：フォールバックはゼロ */ }

            // 合成曲線があれば合成で評価
            if (_combinedCurve != null)
            {
                double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
                double EItan = _combinedCurve.EvaluateTangent(phiRes);
                double EIsec = _combinedCurve.EvaluateSecant(phiRes);

                // v23 (A-1): 接線側フォールバックは 0.001×EI0 に縮小（旧: EI0）
                double EI0fallback = EI0y > 0.0 ? EI0y : EI0z;
                if (!double.IsFinite(EItan) || EItan <= 0.0) EItan = 0.001 * EI0fallback;
                if (!double.IsFinite(EIsec) || EIsec <= 0.0) EIsec = EI0fallback;

                // v24 (B-2): 合成 M-φ の対角 Jacobian ブレンド
                // p–y 曲線と同じ形の真の Jacobian:
                //   dMy/dφy = EI_tan × cos²θ + EI_sec × sin²θ
                //   dMz/dφz = EI_tan × sin²θ + EI_sec × cos²θ
                //   dMy/dφz = dMz/dφy = (EI_tan - EI_sec) × cosθ × sinθ    （本 fix では未反映）
                // ここで cosθ = φy/φres, sinθ = φz/φres。
                // 旧実装は (EI_tan, EI_tan) 等方で、φy / φz の比に関係なく両軸とも EI_tan を使用していた。
                // 新実装は対角ブロックのみ（非対角 off-diagonal 項は現 SetKe 構造で表現不可）を正しく反映し、
                // 曲率主軸に沿う方向は EI_tan、垂直方向は EI_sec を返す。
                // 非対角項が残ることで α が完全に 1.0 にはならないが、等方近似よりは大幅に改善される。
                if (phiRes < 1e-15)
                {
                    // φ ≈ 0: 方向未定、等方 EI_sec（初期剛性相当）で扱う
                    return (EIsec, EIsec);
                }
                double cosT = phiY / phiRes;
                double sinT = phiZ / phiRes;
                double cos2 = cosT * cosT;
                double sin2 = sinT * sinT;
                double EIy = EItan * cos2 + EIsec * sin2;
                double EIz = EItan * sin2 + EIsec * cos2;
                return (EIy, EIz);
            }

            // 個別曲線は未実装。塑性域は初期剛性の 0.001 倍にフォールバック
            // v23 (A-1): 塑性域での初期剛性フォールバックを縮小
            double eiy = 0.001 * EI0y;
            double eiz = 0.001 * EI0z;

            return (eiy, eiz);
        }

        // 要素中央曲率から割線剛性（M/φ）を返す（内力計算用）
        // φ→0では初期接線剛性に収束
        public (double EIy_eff, double EIz_eff) EvaluateEIeffSecant(double phiY, double phiZ)
        {
            // 初期 EI を取得（null 安全）
            double EI0y = 0.0;
            double EI0z = 0.0;
            try
            {
                if (Section?.Material != null)
                {
                    EI0y = Section.Material.E * Section.IY;
                    EI0z = Section.Material.E * Section.IZ;
                }
            }
            catch { /* 無視：フォールバックはゼロ */ }

            // 合成曲線があれば合成で評価
            if (_combinedCurve != null)
            {
                double phiRes = Math.Sqrt(phiY * phiY + phiZ * phiZ);
                double EIeff = _combinedCurve.EvaluateSecant(phiRes);
                if (!double.IsFinite(EIeff) || EIeff <= 0.0) EIeff = (EI0y > 0.0 ? EI0y : EI0z);
                return (EIeff, EIeff);
            }

            // 個別曲線は未実装。初期 EI へフォールバック
            double eiy = EI0y;
            double eiz = EI0z;

            return (eiy, eiz);
        }

        // i端回転
        public double Ryi_tan { get; set; }
        public double Rzi_tan { get; set; }
        public double Ryi_sec { get; set; }
        public double Rzi_sec { get; set; }
        // j端回転
        public double Ryj_tan { get; set; }
        public double Rzj_tan { get; set; }
        public double Ryj_sec { get; set; }
        public double Rzj_sec { get; set; }
        // 曲げ剛性調整係数
        public double KTan_y { get; set; }
        public double KTan_z { get; set; }
        public double KSec_y { get; set; }
        public double KSec_z { get; set; }
        public double EA_multiplier { get; set; }

        public BeamDisp IncrementalDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamDisp CumulativeDisp { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce IncrementalForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        public BeamForce CumulativeForce { get; set; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        public double EA_Multiplier { get; set; }

        // 要素中央の曲率（解析中に更新される）
        public double CurrentCurvature { get; set; }

        // 要素中央のモーメント（M-φ曲線から直接評価、解析中に更新される）
        public double CurrentMoment { get; set; }

        // v28 問題 A 診断: M-φ 曲線のどのセグメントに現在の φ が属するかを追跡。
        // 0 = 弾性 (|φ| < φ_cr), 1 = ひび割れ後, 2 = 降伏後... (Points のインデックス)
        // 反復ごとに更新され、前反復との差分で状態変化を検出する。
        [JsonIgnore]
        public int CurrentMPhiSegmentIndex { get; set; } = -1;
        [JsonIgnore]
        public int PreviousMPhiSegmentIndex { get; set; } = -1;

        [JsonIgnore]
        public Matrix<double> KeTan { get; set; }
        [JsonIgnore]
        public Matrix<double> KeSec { get; set; }
        [JsonIgnore]
        public Vector<double> Curve { get; set; }

        public HorizontalSoilReactionItem HorizontalSoilReactionItem { get; set; }
        public System.Collections.Generic.List<BeamResult> BeamResults { get; set; } = [];

        // パラメータなしコンストラクタ（必須）
        public Beam() { }

        // 生成用コンストラクタ
        public Beam(string name, Section section, Node nodeI, Node nodeJ, double ri, double rj)
        {
            Name = name;
            Section = section;
            NodeI = nodeI;
            NodeJ = nodeJ;
            Length = Utils.GetLengthBetweenTwoNodes(nodeI, nodeJ);

            Ryi_tan = ri;
            Rzi_tan = ri;
            Ryi_sec = ri;
            Rzi_sec = ri;
            Ryj_tan = rj;
            Rzj_tan = rj;
            Ryj_sec = rj;
            Rzj_sec = rj;
            KTan_y = 1.0;
            KTan_z = 1.0;
            KSec_y = 1.0;
            KSec_z = 1.0;
            EA_multiplier = 1.0;
            SetKe(true);
            SetKe(false);
            IncrementalDisp = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            CumulativeDisp = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
        //PLNO = plno; // 杭


        // 梁端部回転剛性をセットする。
        public void SetRotStiffness(float ky, float kz, bool isTan, bool isEndI)
        {
            double _ky = isTan ? KTan_y : KSec_y;
            double _kz = isTan ? KTan_z : KSec_z;

            double eIy_per_L1 = Section.Material.E * Section.IY / Length * ky;
            double eIz_per_L1 = Section.Material.E * Section.IZ / Length * kz;

            if (isTan)
            {
                if (isEndI == true)
                {
                    Ryi_tan = 1 / (1 + 6 * eIy_per_L1 / _ky);
                    Rzi_tan = 1 / (1 + 6 * eIz_per_L1 / _kz);
                }
                else if (isEndI == false)
                {
                    Ryj_tan = 1 / (1 + 6 * eIy_per_L1 / _ky);
                    Rzj_tan = 1 / (1 + 6 * eIz_per_L1 / _kz);
                }
            }
            else
            {
                if (isEndI == true)
                {
                    Ryi_sec = 1 / (1 + 6 * eIy_per_L1 / _ky);
                    Rzi_sec = 1 / (1 + 6 * eIz_per_L1 / _kz);
                }
                else if (isEndI == false)
                {
                    Ryj_sec = 1 / (1 + 6 * eIy_per_L1 / _ky);
                    Rzj_sec = 1 / (1 + 6 * eIz_per_L1 / _kz);
                }
            }
        }

        // 要素剛性補正係数を取得する
        public void SetMultiplier(double eA_multiplier)
        {
            EA_Multiplier = eA_multiplier;
        }

        // 要素剛性を取得する
        public void SetKe(bool isTan)
        {
            double k_y = isTan ? KTan_y : KSec_y;
            double k_z = isTan ? KTan_z : KSec_z;
            double ryi = isTan ? Ryi_tan : Ryi_sec;
            double ryj = isTan ? Ryj_tan : Ryj_sec;
            double rzi = isTan ? Rzi_tan : Rzi_sec;
            double rzj = isTan ? Rzj_tan : Rzj_sec;

            double ryb = 1 + ryi + ryj;
            double rzb = 1 + rzi + rzj;

            double eA_per_L = Section.Material.E * Section.AX / Length;　// Nmm >> kNm
            double gJ_per_L = Section.Material.G * Section.IX / Length;　// Nmm >> kNm
            double eIy_per_L1 = Section.Material.E * Section.IY / Length * k_y;
            double eIz_per_L1 = Section.Material.E * Section.IZ / Length * k_z;
            // 修正: k_y/k_z は一度だけ掛ける（以前は二重・三重に掛けていた）
            double eIy_per_L2 = eIy_per_L1 / Length;
            double eIz_per_L2 = eIz_per_L1 / Length;
            double eIy_per_L3 = eIy_per_L2 / Length;
            double eIz_per_L3 = eIz_per_L2 / Length;

            // z軸まわりの曲げ
            double eIz_per_L3_RM1 = eIz_per_L3 * (rzi + rzj + 4 * rzi * rzj) / rzb;
            // [ 1, 1], [ 7, 7], [ 7, 1], [ 1, 7]
            double eIz_per_L2_RM1 = eIz_per_L2 * rzi * (1 + 2 * rzj) / rzb;
            // [ 5, 1], [ 1, 5], [ 7, 5], [ 5, 7]
            double eIz_per_L2_RM2 = eIz_per_L2 * (1 + 2 * rzi) * rzj / rzb;
            // [11, 1], [ 1,11], [11, 7], [ 7,11]
            double eIz_per_L1_RM1 = eIz_per_L1 * rzi * (1 + rzj) / rzb;
            // [ 5, 5]
            double eIz_per_L1_RM2 = eIz_per_L1 * (1 + rzi) * rzj / rzb;
            // [11,11]
            double eIz_per_L1_RM3 = eIz_per_L1 * rzi * rzj / rzb;
            // [11, 5], [ 5,11]

            // y軸まわりの曲げ
            double eIy_per_L3_RM1 = eIy_per_L3 * (ryi + ryj + 4 * ryi * ryj) / ryb;
            // [ 2, 2], [ 8, 8], [ 8, 2], [ 2, 8]
            double eIy_per_L2_RM1 = eIy_per_L2 * ryi * (1 + 2 * ryj) / ryb;
            // [ 4, 2], [ 2, 4], [ 8, 4], [ 4, 8]
            double eIy_per_L2_RM2 = eIy_per_L2 * (1 + 2 * ryi) * ryj / ryb;
            // [10, 2], [ 2,10], [10, 8], [ 8,10]
            double eIy_per_L1_RM1 = eIy_per_L1 * ryi * (1 + ryj) / ryb;
            // [ 4, 4]
            double eIy_per_L1_RM2 = eIy_per_L1 * (1 + ryi) * ryj / ryb;
            // [10,10]
            double eIy_per_L1_RM3 = eIy_per_L1 * ryi * ryj / ryb;
            // [10, 4], [ 4,10]
            // 0/3/6/9 // 1/4/7/10 // 2/5/8/11
            //

            Matrix<double> ke = Matrix<double>.Build.DenseOfArray(new double[,]
{
                { eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, -eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 0

                { 0.0, 6.0 * eIz_per_L3_RM1, 0.0, 0.0, 0.0, 6.0 * eIz_per_L2_RM1,
                    0.0, -6.0 * eIz_per_L3_RM1, 0.0, 0.0, 0.0, 6.0 * eIz_per_L2_RM2 }, // 1

                { 0.0, 0.0, 6.0 * eIy_per_L3_RM1, 0.0, -6.0 * eIy_per_L2_RM1, 0.0,
                    0.0, 0.0, -6.0 * eIy_per_L3_RM1, 0.0, -6.0 * eIy_per_L2_RM2, 0.0 }, // 2

                { 0.0, 0.0, 0.0, gJ_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, -gJ_per_L, 0.0, 0.0 }, // 3

                { 0.0, 0.0, -6.0 * eIy_per_L2_RM1, 0.0, 6.0 * eIy_per_L1_RM1, 0.0,
                    0.0, 0.0, 6.0 * eIy_per_L2_RM1, 0.0, 6.0 * eIy_per_L1_RM3, 0.0 }, // 4

                { 0.0, 6.0 * eIz_per_L2_RM1, 0.0, 0.0, 0.0, 6.0 * eIz_per_L1_RM1,
                    0.0, -6.0 * eIz_per_L2_RM1, 0.0, 0.0, 0.0, 6.0 * eIz_per_L1_RM3 }, // 5

                { -eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, eA_per_L, 0.0, 0.0, 0.0, 0.0, 0.0 }, // 6

                { 0.0, -6.0 * eIz_per_L3_RM1, 0.0, 0.0, 0.0, -6.0 * eIz_per_L2_RM1,
                    0.0, 6.0 * eIz_per_L3_RM1, 0.0, 0.0, 0.0, -6.0 * eIz_per_L2_RM2 }, // 7

                { 0.0, 0.0, -6.0 * eIy_per_L3_RM1, 0.0, 6.0 * eIy_per_L2_RM1, 0.0,
                    0.0, 0.0, 6.0 * eIy_per_L3_RM1, 0.0, 6.0 * eIy_per_L2_RM2, 0.0 }, // 8

                { 0.0, 0.0, 0.0, -gJ_per_L, 0.0, 0.0, 0.0, 0.0, 0.0, gJ_per_L, 0.0, 0.0 }, // 9

                { 0.0, 0.0, -6.0 * eIy_per_L2_RM2, 0.0, 6.0 * eIy_per_L1_RM3, 0.0,
                    0.0, 0.0, 6.0 * eIy_per_L2_RM2, 0.0, 6.0 * eIy_per_L1_RM2, 0.0 }, // 10

                { 0.0, 6.0 * eIz_per_L2_RM2, 0.0, 0.0, 0.0, 6.0 * eIz_per_L1_RM3,
                    0.0, -6.0 * eIz_per_L2_RM2, 0.0, 0.0, 0.0, 6.0 * eIz_per_L1_RM2 } // 11
                });

            if (isTan)
            {
                KeTan = ke;
            }
            else
            {
                KeSec = ke;
            }
        }

        // 座標変換マトリクスのキャッシュ（解析中ノード座標は不変）
        private Matrix<double> _cachedCoordTransform;

        public Matrix<double> GetCachedCoordTransform()
        {
            return _cachedCoordTransform ??= Utils.GetTransformMatrix(NodeI, NodeJ);
        }

        public void ClearCachedCoordTransform() => _cachedCoordTransform = null;

        // 要素剛性を部材座標系から全体座標系に変換する
        public Matrix<double> TransElemStiffToGlobal(bool isTan)
        {
            Matrix<double> ke = isTan ? KeTan : KeSec;
            Matrix<double> t = GetCachedCoordTransform();
            Matrix<double> kt = ke * t; // [Ke][t]
            Matrix<double> tkt = t.Transpose() * kt; // [t]-1[Ke][t] 逆行列
            return tkt;
        }

        // 要素剛性を全体剛性にマップオンする
        public Matrix<double> MapOnGlobalStiff(
            Matrix<double> matrixGlobalStiffness, bool isTan, bool isRowFree, bool isColFree/*, AnaModel anaModel*/)
        {
            // eqリスト作成
            var eq = Utils.GetEquationNumbers(NodeI, NodeJ) /*, anaModel)*/;
            // 要素剛性マトリクス（部材座標→全体座標に変換済み）
            Matrix<double> tkt = TransElemStiffToGlobal(isTan);

            // 共通関数で全体剛性マトリクスへ加算
            Utils.AddStiffnessToGlobal(matrixGlobalStiffness, tkt, eq, isRowFree, isColFree, NodeI, NodeJ/*, anaModel*/);

            return matrixGlobalStiffness;
        }

        // 要素応力の計算
        public Vector<double> CalcBeamForce(bool isTan)
        {
            // 節点の変位ベクトル（全体座標系）

            Vector<double> dispNodeI = NodeI.CumulativeDisp.GetVector(); //double[] dispNodeI = nodeI.OutNode.Disp;
            Vector<double> dispNodeJ = NodeJ.CumulativeDisp.GetVector(); //double[] dispNodeJ = nodeJ.OutNode.Disp;

            // dispNodeIとdispNodeJを組み合わせて2n行のベクトルを作成
            Vector<double> disp = Vector<double>.Build.Dense(dispNodeI.Count + dispNodeJ.Count); //double[] disp = [.. dispNodeI, .. dispNodeJ];
            disp.SetSubVector(0, dispNodeI.Count, dispNodeI);
            disp.SetSubVector(dispNodeI.Count, dispNodeJ.Count, dispNodeJ);

            // 節点の変位ベクトル（要素座標系）

            Matrix<double> t = Utils.GetTransformMatrix(NodeI, NodeJ); //double[,] t = Utils.GetTransformMatrix(nodeI, nodeJ);
            Vector<double> d = t * disp; //double[] d = Utils.MatrixVectorMultiply(t, disp);
            // 要素応力 f = k * d
            Matrix<double> k = (isTan ? KeTan : KeSec) ?? throw new InvalidOperationException("Stiffness matrix is not initialized. Call SetKe() before CalcInternalForce().");
            // 内力計算
            Vector<double> f = k * d; //double[] f = Utils.MatrixVectorMultiply(k, d);
                                      // 要素応力を結果用オブジェクトにセット
            return f;

        }

        // 要素変位、要素応力のセットメソッド
        public void SetBeamDispAndForce(bool isTan = false)
        {
            // 節点の変位ベクトル（全体座標系）

            Vector<double> dispNodeI = NodeI.CumulativeDisp.GetVector(); //double[] dispNodeI = nodeI.OutNode.Disp;
            Vector<double> dispNodeJ = NodeJ.CumulativeDisp.GetVector(); //double[] dispNodeJ = nodeJ.OutNode.Disp;

            // dispNodeIとdispNodeJを組み合わせて2n行のベクトルを作成
            Vector<double> disp = Vector<double>.Build.Dense(dispNodeI.Count + dispNodeJ.Count); //double[] disp = [.. dispNodeI, .. dispNodeJ];
            disp.SetSubVector(0, dispNodeI.Count, dispNodeI);
            disp.SetSubVector(dispNodeI.Count, dispNodeJ.Count, dispNodeJ);

            // 節点の変位ベクトル（要素座標系）

            Matrix<double> t = Utils.GetTransformMatrix(NodeI, NodeJ); //double[,] t = Utils.GetTransformMatrix(nodeI, nodeJ);
            Vector<double> d = t * disp; //double[] d = Utils.MatrixVectorMultiply(t, disp);
            // 要素応力 f = k * d
            Matrix<double> k = (isTan ? KeTan : KeSec) ?? throw new InvalidOperationException("Stiffness matrix is not initialized. Call SetKe() before CalcInternalForce().");
            // 内力計算
            Vector<double> f = k * d; //double[] f = Utils.MatrixVectorMultiply(k, d);

            // NaN診断: 要素内力のNaN検出
            bool fHasNaN = false;
            for (int fi = 0; fi < f.Count; fi++)
            {
                if (!double.IsFinite(f[fi])) { fHasNaN = true; break; }
            }
            if (fHasNaN)
            {
                // 原因を特定: 変位d、剛性k、変換T、節点変位のどれが汚染源か
                bool dispHasNaN = false, keHasNaN = false;
                for (int di2 = 0; di2 < d.Count; di2++)
                    if (!double.IsFinite(d[di2])) { dispHasNaN = true; break; }
                for (int ki = 0; ki < Math.Min(12, k.RowCount); ki++)
                    if (!double.IsFinite(k[ki, ki])) { keHasNaN = true; break; }

                /* NaNDiagnostics.LogNaN(
                    $"Beam '{Name}' force NaN! dispNaN={dispHasNaN}, keNaN={keHasNaN}, " +
                    $"NodeI='{NodeI?.Name}' NodeJ='{NodeJ?.Name}' " +
                    $"NodeI.disp=[{NodeI?.CumulativeDisp?.Ux:E3},{NodeI?.CumulativeDisp?.Uy:E3},{NodeI?.CumulativeDisp?.Uz:E3}," +
                    $"{NodeI?.CumulativeDisp?.Rx:E3},{NodeI?.CumulativeDisp?.Ry:E3},{NodeI?.CumulativeDisp?.Rz:E3}] " +
                    $"NodeJ.disp=[{NodeJ?.CumulativeDisp?.Ux:E3},{NodeJ?.CumulativeDisp?.Uy:E3},{NodeJ?.CumulativeDisp?.Uz:E3}," +
                    $"{NodeJ?.CumulativeDisp?.Rx:E3},{NodeJ?.CumulativeDisp?.Ry:E3},{NodeJ?.CumulativeDisp?.Rz:E3}]"); */
            }

            // 要素応力を結果用オブジェクトにセット
            CumulativeDisp.SetVector(d);
            CumulativeForce.SetVector(f);
        }

        // 断面力をセットする
        public void SetForce(Vector<double> df)
        {
            IncrementalForce.SetVector(df);
            CumulativeForce += IncrementalForce;
        }

        // 初期断面力のセット
        public void InitializeForce()
        {
            IncrementalForce = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            CumulativeForce = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        // 初期要素変位のセット
        public void InitializeDisp()
        {
            IncrementalDisp = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            CumulativeDisp = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        // 初期曲率状態のセット
        public void SetInitialCurve()
        {
            Curve = Vector<double>.Build.Dense(2, 0.0);
        }

        // 梁要素の解析結果を取得する
        public BeamResult GetBeamResult(AnaModel anaModel, LoadCase loadCase, LoadCombination loadCombination, bool isLiquefaction, int step = -1)
        {
            if (step == -1)
            {
                step = anaModel.GetAnalysisLastStep(loadCase, loadCombination, isLiquefaction);
            }

            foreach (BeamResult beamResult in BeamResults)
            {
                // 名前ベースで比較（参照比較の代わりに）
                bool loadCaseMatch = loadCase?.LoadName == beamResult.LoadCase?.LoadName;
                bool loadCombMatch = loadCombination?.Name == beamResult.LoadCombination?.Name;
                bool isLiqMatch = isLiquefaction == beamResult.IsLiquefaction;
                bool stepMatch = step == beamResult.Step;

                if (loadCaseMatch && loadCombMatch && isLiqMatch && stepMatch)
                { return beamResult; }
            }

            // フォールバック: 逆の液状化状態で検索（液状化トグルの状態と解析結果が異なる場合の救済）
            int fallbackStep = anaModel.GetAnalysisLastStep(loadCase, loadCombination, !isLiquefaction);
            foreach (BeamResult beamResult in BeamResults)
            {
                bool loadCaseMatch = loadCase?.LoadName == beamResult.LoadCase?.LoadName;
                bool loadCombMatch = loadCombination?.Name == beamResult.LoadCombination?.Name;
                bool isLiqMatch = !isLiquefaction == beamResult.IsLiquefaction;
                bool stepMatch = fallbackStep == beamResult.Step;

                if (loadCaseMatch && loadCombMatch && isLiqMatch && stepMatch)
                { return beamResult; }
            }

            return null;
        }

        // 杭先端接続フラグをセットする
        public void SetPileTopFlag(bool isPileTop)
        {
            IsPileTop = isPileTop;
        }


        public Beam DeepCopy()
        {
            var sectionCopy = Section?.DeepCopy();
            var nodeICopy = NodeI?.DeepCopy();
            var nodeJCopy = NodeJ?.DeepCopy();
            var soilReactionCopy = HorizontalSoilReactionItem?.DeepCopy();

            var copy = new Beam(Name, sectionCopy, nodeICopy, nodeJCopy, Ryi_tan, Ryj_tan)
            {
                IsPileTop = this.IsPileTop,
                IsPileHeadElement = this.IsPileHeadElement,
                // E3c-2-fix (2026-04-23): PileBodyNo / SegmentIndex を copy 漏れ していたため、
                // DeepCopy された caseModel では SetupMPhiFromPileSectionForLoadCase で全 beam が
                // "skippedNoPileBody" として弾かれ M-φ 曲線がセットされず K 行列が divergence していた
                PileBodyNo = this.PileBodyNo,
                SegmentIndex = this.SegmentIndex,
                Length = this.Length,
                Ryi_tan = this.Ryi_tan,
                Rzi_tan = this.Rzi_tan,
                Ryi_sec = this.Ryi_sec,
                Rzi_sec = this.Rzi_sec,
                Ryj_tan = this.Ryj_tan,
                Rzj_tan = this.Rzj_tan,
                Ryj_sec = this.Ryj_sec,
                Rzj_sec = this.Rzj_sec,
                KTan_y = this.KTan_y,
                KTan_z = this.KTan_z,
                KSec_y = this.KSec_y,
                KSec_z = this.KSec_z,
                EA_multiplier = this.EA_multiplier,
                EA_Multiplier = this.EA_Multiplier,
                CurrentCurvature = this.CurrentCurvature,
                CurrentMoment = this.CurrentMoment,
                CurrentMPhiSegmentIndex = this.CurrentMPhiSegmentIndex,
                PreviousMPhiSegmentIndex = this.PreviousMPhiSegmentIndex,
                IncrementalDisp = this.IncrementalDisp?.Clone(),
                CumulativeDisp = this.CumulativeDisp?.Clone(),
                IncrementalForce = this.IncrementalForce?.Clone(),
                CumulativeForce = this.CumulativeForce?.Clone(),
                KeTan = this.KeTan?.Clone(),
                KeSec = this.KeSec?.Clone(),
                Curve = this.Curve?.Clone(),
                HorizontalSoilReactionItem = soilReactionCopy,
                BeamResults = [.. this.BeamResults.Select(r => r.DeepCopy())]
            };
            return copy;
        }
    }
}



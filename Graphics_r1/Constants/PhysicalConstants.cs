using System;

namespace PileDesign.Constants;

/// <summary>
/// 数値計算用の許容誤差定数
/// </summary>
public static class NumericalConstants
{
    /// <summary>座標マッチング用の許容誤差</summary>
    public const double COORDINATE_TOLERANCE = 1e-6;

    /// <summary>曲率計算用の許容誤差</summary>
    public const double CURVATURE_TOLERANCE = 1e-12;

    /// <summary>変位量の微小判定用許容誤差（ピコメートルオーダー）</summary>
    public const double SMALL_DISPLACEMENT_EPSILON = 1e-12;

    /// <summary>行列・ベクトル特異性判定用の微小許容誤差</summary>
    public const double NEAR_ZERO_EPSILON = 1e-10;

    /// <summary>座標グリッド比較用の許容誤差（ミリメートルオーダー）</summary>
    public const double MINOR_LENGTH_EPSILON = 0.001;

    /// <summary>力・モーメント計算用の許容誤差</summary>
    public const double FORCE_TOLERANCE = 1e-9;

    /// <summary>時間差分の許容誤差</summary>
    public const double TIME_DELTA_TOLERANCE = 1e-20;

    /// <summary>収束判定用の許容誤差</summary>
    public const double CONVERGENCE_TOLERANCE = 1e-8;

    /// <summary>PCD（ピッチ円直径）比較用の許容誤差</summary>
    public const double PCD_COMPARISON_TOLERANCE = 1e-6;
}

/// <summary>
/// 単位変換係数
/// </summary>
public static class UnitConversion
{
    /// <summary>ミリメートルからメートルへの変換係数 (1mm = 0.001m)</summary>
    public const double MM_TO_M = 0.001;

    /// <summary>メートルからミリメートルへの変換係数 (1m = 1000mm)</summary>
    public const double M_TO_MM = 1000.0;

    /// <summary>キロニュートンからニュートンへの変換係数 (1kN = 1000N)</summary>
    public const double KN_TO_N = 1000.0;

    /// <summary>ニュートンからキロニュートンへの変換係数 (1N = 0.001kN)</summary>
    public const double N_TO_KN = 0.001;

    // ─── M-φ パイプライン用（断面計算 [N, mm] ⇔ FEM/表示 [kN, m]）───
    // 過去に kN/N 混同で M-φ が 1/1000 になる実バグがあった系統。変換は必ず本定数を使うこと。

    /// <summary>曲げモーメント N·mm → kN·m (×1e-6)</summary>
    public const double NMM_TO_KNM = 1e-6;

    /// <summary>曲げモーメント kN·m → N·mm (×1e6)</summary>
    public const double KNM_TO_NMM = 1e6;

    /// <summary>曲率 1/mm → 1/m (=rad/m, ×1000)</summary>
    public const double PER_MM_TO_PER_M = 1000.0;

    /// <summary>曲率 1/m → 1/mm (×0.001)</summary>
    public const double PER_M_TO_PER_MM = 0.001;

    /// <summary>
    /// 質量 t → 力 kN の変換係数（標準重力加速度 9.80665 m/s²）。
    /// メーカーカタログが質量 [t/m] で与えられる製品（PHC節杭の標準質量など）を
    /// 自重 [kN/m] に直すのに使う。
    /// </summary>
    public const double TON_TO_KN = 9.80665;
}

/// <summary>
/// 断面ソルバ（終局曲げ・ひび割れ・ファイバー掃引）の軸力残差の収束許容値。
///
/// 歴史的にソルバごとに値が 3 桁異なる（0.1 N / max(1, 1e-3·|N|) / max(100, 1e-6·|N|)）。
/// 挙動保存のため現状値を名前付きで固定した。真の統一（絶対+相対の共通ポリシー）は
/// 収束リグレッション・耐力曲線スナップショットへの影響評価とセットで行うこと。
/// </summary>
public static class SectionSolverTolerances
{
    /// <summary>終局曲げソルバの軸力残差許容 [N]（GetUltimateMomentForSpecificN 系）</summary>
    public const double ULTIMATE_AXIAL_RESIDUAL_N = 0.1;

    /// <summary>ひび割れモーメントソルバの軸力残差許容: max(CRACK_AXIAL_ABS_N, CRACK_AXIAL_REL·|N|) [N]</summary>
    public const double CRACK_AXIAL_ABS_N = 1.0;

    /// <summary>ひび割れモーメントソルバの軸力残差の相対許容</summary>
    public const double CRACK_AXIAL_REL = 1e-3;

    /// <summary>ファイバー M-φ 掃引の軸力つり合い許容: max(FIBER_AXIAL_ABS_N, FIBER_AXIAL_REL·|N|) [N]</summary>
    public const double FIBER_AXIAL_ABS_N = 100.0;

    /// <summary>ファイバー M-φ 掃引の軸力つり合いの相対許容</summary>
    public const double FIBER_AXIAL_REL = 1e-6;
}

/// <summary>
/// 「基礎部材の強度と変形性能」（日本建築学会、第1版 2022年）由来の断面設計定数
/// </summary>
public static class SectionDesignConstants
{
    /// <summary>
    /// コンクリートの終局（安全限界）圧縮縁ひずみ εcu = 0.003。
    /// 安全限界曲げの算定・バイリニア/e関数構成則の有効範囲上限・ファイバー掃引の εc 上限に共通。
    /// </summary>
    public const double ULTIMATE_COMPRESSIVE_STRAIN = 0.003;

    /// <summary>
    /// KCTB 場所打ち鋼管コンクリート杭（TB工法）の終局圧縮縁ひずみ εcu = 0.005。
    ///
    /// 出典: 建設省総合技術開発プロジェクト「新建築構造体系の開発」性能評価分科会
    /// 基礎WG 最終報告書（平成12年3月、建設省建築研究所）資料4-7
    /// 「場所打ち鋼管コンクリート杭の限界ひずみと M-φ 関係」。
    ///
    /// φ800×t9 SKK400 の内面リブ・突起付き鋼管に現場施工でコンクリートを充填した
    /// 実大試験体 2 体の正負交番繰り返し曲げ試験と断面解析の照合により、
    /// 3,000μ では鋼管の拘束効果が考慮されず限界曲率を過小評価し、
    /// 7,000μ では実験値を上回る（危険側）ため、5,000μ が安全側と結論されている。
    /// </summary>
    public const double KCTB_ULTIMATE_COMPRESSIVE_STRAIN = 0.005;
}

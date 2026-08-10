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
}

/// <summary>
/// 幾何計算・角度変換定数
/// </summary>
public static class GeometricConstants
{
    /// <summary>度からラジアンへの変換係数 (π/180)</summary>
    public const double DEG_TO_RAD = Math.PI / 180.0;

    /// <summary>ラジアンから度への変換係数 (180/π)</summary>
    public const double RAD_TO_DEG = 180.0 / Math.PI;

    // 標準角度定数
    public const double ANGLE_0_DEG = 0.0;
    public const double ANGLE_45_DEG = 45.0;
    public const double ANGLE_90_DEG = 90.0;
    public const double ANGLE_180_DEG = 180.0;
    public const double ANGLE_270_DEG = 270.0;
    public const double ANGLE_360_DEG = 360.0;

    // 比率・係数
    /// <summary>1/2 (半分)</summary>
    public const double HALF = 0.5;

    /// <summary>1/4 (4分の1)</summary>
    public const double QUARTER = 0.25;

    /// <summary>3/4 (4分の3)</summary>
    public const double THREE_QUARTERS = 0.75;

    // 円形断面計算用係数
    /// <summary>円形断面積計算係数 (A = π/4 × D²)</summary>
    public const double CIRCLE_AREA_FACTOR = Math.PI / 4.0;

    /// <summary>円形断面係数計算係数 (Z = π/32 × D³)</summary>
    public const double CIRCLE_SECTION_MODULUS_FACTOR = Math.PI / 32.0;

    /// <summary>円形断面二次モーメント計算係数 (I = π/64 × D⁴)</summary>
    public const double CIRCLE_MOMENT_INERTIA_FACTOR = Math.PI / 64.0;

    /// <summary>円周計算係数 (2π)</summary>
    public const double TWO_PI = 2.0 * Math.PI;
}

/// <summary>
/// タイミング・遅延時間定数
/// </summary>
public static class TimingConstants
{
    /// <summary>SoilPiles生成のデバウンス時間 (ミリ秒)</summary>
    public const int SOIL_PILES_DEBOUNCE_MS = 50;

    /// <summary>ウィンドウ更新のデバウンス時間 (ミリ秒)</summary>
    public const int WINDOW_UPDATE_DEBOUNCE_MS = 30;

    /// <summary>読み込みスプラッシュ画面の表示時間 (ミリ秒)</summary>
    public const int LOADING_SPLASH_DURATION_MS = 3000;

    /// <summary>長時間処理の遅延時間 (ミリ秒)</summary>
    public const int LONG_OPERATION_DELAY_MS = 5000;

    /// <summary>UI更新の遅延時間 (ミリ秒)</summary>
    public const int UI_REFRESH_DELAY_MS = 50;

    /// <summary>ダイアログ遷移の遅延時間 (ミリ秒)</summary>
    public const int DIALOG_TRANSITION_DELAY_MS = 300;

    /// <summary>コントロール描画の遅延時間 (ミリ秒)</summary>
    public const int CONTROL_RENDER_DELAY_MS = 100;
}

/// <summary>
/// 解析パラメータ定数
/// </summary>
public static class AnalysisConstants
{
    /// <summary>荷重レベル1のデフォルト値 (kN)</summary>
    public const double DEFAULT_LOAD_LEVEL1 = 1000.0;

    /// <summary>荷重レベル2のデフォルト値 (kN)</summary>
    public const double DEFAULT_LOAD_LEVEL2 = 2000.0;

    /// <summary>デフォルトモーメント値 (kNm)</summary>
    public const double DEFAULT_MOMENT = 10.0;

    // 収束判定許容誤差は NumericalConstants.CONVERGENCE_TOLERANCE を参照すること。
    // （以前ここに重複定義があったが 2026-04-18 に削除）

    /// <summary>反復計算の最大回数</summary>
    public const int MAX_ITERATIONS = 1000;
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
}

/// <summary>
/// UI表示関連定数
/// </summary>
public static class UIConstants
{
    /// <summary>右側の余白幅 (ピクセル)</summary>
    public const double RIGHT_BLANK_WIDTH_PX = 100.0;

    /// <summary>標準スペーシング値</summary>
    public const int STANDARD_SPACING = 45;
}

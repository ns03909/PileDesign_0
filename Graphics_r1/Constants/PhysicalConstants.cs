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
/// UI表示関連定数
/// </summary>
public static class UIConstants
{
    /// <summary>右側の余白幅 (ピクセル)</summary>
    public const double RIGHT_BLANK_WIDTH_PX = 100.0;

    /// <summary>標準スペーシング値</summary>
    public const int STANDARD_SPACING = 45;
}

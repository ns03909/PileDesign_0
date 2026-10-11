using PileDesign.Models.InputData;
using System.Collections.Generic;

namespace PileDesign.Services
{
    /// <summary>
    /// 照査の種類。限界状態の割り当てが耐震性能グレードに従うかどうかが種類で違う。
    /// </summary>
    public enum LimitCheckKind
    {
        /// <summary>杭体の曲げ・せん断 (N-M・Q-N 曲線)。</summary>
        SectionForce,
        /// <summary>杭頭 2 点間の変形角。</summary>
        DeformationAngle,
        /// <summary>杭の鉛直支持力 (押込み・引抜き)。</summary>
        Bearing,
        /// <summary>基礎梁の傾斜角。</summary>
        FoundationBeamInclination,
        /// <summary>
        /// 杭頭回転角。限界値は杭頭工法ごとの規定 (θu・θa) で、照査するレベルも工法で決まる。
        /// <b>耐震グレードに依らず</b>レベル1 = 損傷限界、レベル2 = 安全限界と名乗る (従来どおり)。
        /// </summary>
        PileHeadRotation,
    }

    /// <summary>
    /// 限界状態の割り当て。<paramref name="DamageLevel"/> は損傷限界の曲線を選ぶレベル
    /// (レベル1 は β2 を乗じない、レベル2 は β1×β2)。損傷限界以外では曲線に効かないので 2 を入れておく
    /// (<see cref="PileSection.ComputeQNForMonQd"/> などの既定値と同じ)。
    /// </summary>
    public readonly record struct LimitStateAssignment(SectionLimitState Limit, int DamageLevel);

    /// <summary>
    /// <b>耐震性能グレード × 荷重レベル → 限界状態</b>の規則。判定はここ 1 か所。
    ///
    /// <code>
    ///   長期 (VL, レベル0)   使用限界
    ///   レベル1              損傷限界 (損傷限界の曲線はレベル1)
    ///   レベル2  グレードA   安全限界
    ///   レベル2  グレードS   損傷限界 (損傷限界の曲線はレベル2)
    /// </code>
    ///
    /// グレードが未設定・想定外の値のときは <b>A</b> として扱う (検定がそうしていた)。
    ///
    /// 以前はこの規則が検定 (EvaluationService)・支持力・基礎梁の傾斜角・画面のグラフ・計算書の図と前提の表に
    /// 8 か所以上書き写されていて、写しごとに既定値と損傷限界のレベルの取り方が違った
    /// (画面のグラフは「A でなければ S」、計算書と検定は「S でなければ A」。
    /// 計算書の N-M・Q-N 図はグレードS でレベル1 の点にもレベル2 の損傷限界の曲線だけを描いていた)。
    /// </summary>
    public static class LimitStateRule
    {
        /// <summary>グレードが未設定・想定外の値のときに使うグレード。</summary>
        public const string DefaultGrade = "A";

        /// <summary>グレードS (レベル2 も損傷限界で照査する) か。それ以外はすべて A として扱う。</summary>
        public static bool IsGradeS(string? grade) => grade == "S";

        /// <summary>規則で使うグレード ("S" か "A")。</summary>
        public static string NormalizeGrade(string? grade) => IsGradeS(grade) ? "S" : DefaultGrade;

        /// <summary>基本設定の耐震性能グレード (規則で使う形: "S" か "A")。</summary>
        public static string GradeOf(FundamentalInput? fundamental) => NormalizeGrade(fundamental?.SeismicGrade);

        /// <summary>
        /// 荷重レベル (0 = 長期, 1, 2) と照査の種類から、照査する限界状態と損傷限界の曲線のレベルを返す。
        /// </summary>
        public static LimitStateAssignment For(string? grade, int level, LimitCheckKind kind = LimitCheckKind.SectionForce)
        {
            if (level <= 0) return new(SectionLimitState.Service, 2);
            if (level == 1) return new(SectionLimitState.Damage, 1);

            // レベル2
            bool damage = kind != LimitCheckKind.PileHeadRotation && IsGradeS(grade);
            return damage
                ? new(SectionLimitState.Damage, 2)
                : new(SectionLimitState.Ultimate, 2);
        }

        /// <inheritdoc cref="For(string?, int, LimitCheckKind)"/>
        public static LimitStateAssignment For(FundamentalInput? fundamental, int level, LimitCheckKind kind = LimitCheckKind.SectionForce)
            => For(fundamental?.SeismicGrade, level, kind);

        /// <summary>
        /// 損傷限界の曲線を、荷重レベルから選ぶときのレベル (レベル2 なら 2、それ以外は 1)。
        /// 利用者が限界状態を選んで描く図 (杭の応力の縦断図) はこれで選ぶ。検定の損傷限界と同じ。
        /// </summary>
        public static int DamageCurveLevel(int loadLevel) => loadLevel >= 2 ? 2 : 1;

        /// <summary>
        /// 損傷限界で照査する荷重レベル。グレードA はレベル1 だけ、グレードS はレベル1 と 2。
        /// 全レベルの点を 1 枚に描く図 (N-M・Q-N 相関図) は、ここに挙がるレベルの損傷限界の曲線を描く
        /// (グレードS のレベル1 の点はレベル1 の曲線で照査しているので、レベル2 の曲線だけでは検定と食い違う)。
        /// </summary>
        public static IReadOnlyList<int> DamageCheckedLevels(string? grade)
            => IsGradeS(grade) ? [1, 2] : [1];

        /// <summary>
        /// 全レベルの点を 1 枚に描く図で、主に描く損傷限界の曲線のレベル (損傷限界で照査する最も高いレベル。
        /// グレードA は 1、グレードS は 2)。それ以外の <see cref="DamageCheckedLevels"/> の曲線は書き足す。
        /// </summary>
        public static int PrimaryDamageLevel(string? grade) => IsGradeS(grade) ? 2 : 1;

        /// <summary>レベル2 で安全限界を照査するか (グレードA)。</summary>
        public static bool ChecksUltimate(string? grade)
            => For(grade, 2).Limit == SectionLimitState.Ultimate;

        /// <summary>限界状態の名前 (「使用限界」「損傷限界」「安全限界」)。</summary>
        public static string Name(SectionLimitState limit) => limit switch
        {
            SectionLimitState.Service => "使用限界",
            SectionLimitState.Damage => "損傷限界",
            _ => "安全限界",
        };

        /// <summary>
        /// 画面の「限界状態」の選択肢 (「低減後損傷限界状態」など) を、低減の有無と限界状態に読み替える。
        /// 選択肢の文字列から限界状態を読むのはここだけにする。「なし」や知らない文字列は false。
        /// </summary>
        public static bool TryParseCurveOption(string? option, out bool factored, out SectionLimitState limit)
        {
            factored = false;
            limit = SectionLimitState.Service;
            switch (option)
            {
                case "低減前使用限界状態": factored = false; limit = SectionLimitState.Service; return true;
                case "低減後使用限界状態": factored = true; limit = SectionLimitState.Service; return true;
                case "低減前損傷限界状態": factored = false; limit = SectionLimitState.Damage; return true;
                case "低減後損傷限界状態": factored = true; limit = SectionLimitState.Damage; return true;
                case "低減前安全限界状態": factored = false; limit = SectionLimitState.Ultimate; return true;
                case "低減後安全限界状態": factored = true; limit = SectionLimitState.Ultimate; return true;
                default: return false;
            }
        }
    }
}

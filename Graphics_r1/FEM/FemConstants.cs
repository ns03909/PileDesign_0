namespace PileDesign.FEM
{
    /// <summary>
    /// FEM 解析で使用する数値定数を一元管理する。
    ///
    /// ここに集約する基準:
    ///   (1) 同じ数値が複数箇所で同じ意味で使われている
    ///   (2) 物理的・数値的に明確な意味を持つ
    ///   (3) チューニング時に一括変更したい
    ///
    /// 文脈依存で意味が変わる ε リテラル (1e-15 等の "near zero" ガード) は対象外。
    /// 個別のローカル const で意図を明示する方が読みやすいケースが多いため。
    /// </summary>
    internal static class FemConstants
    {
        // ───────────────────────────────────────────────────────────────
        // Penalty stiffness — 剛体相当ばね・RigidLink 用
        // ───────────────────────────────────────────────────────────────

        /// <summary>
        /// RigidLink (CapNode ↔ ConnectionNode 等) の材料 Young 係数 [kN/m²]。
        /// 2026-05-06: 1e10 → 1e9 (= 1,000 GPa、鋼の約 5 倍) に低減。K 行列の条件数を改善し、
        /// 大規模 RigidLink 連鎖時の数値破綻を緩和。
        /// </summary>
        public const double RigidLinkYoungModulus = 1e9;

        /// <summary>
        /// 並進ペナルティ剛性 (CapNode-PileNode 等の並進拘束用) [kN/m]。
        /// 2026-05-06: DOF 別 Kbig を採用。実杭の並進剛性 (1e6 オーダー) の 10× 余裕。
        /// </summary>
        public const double KbigTranslation = 1e7;

        /// <summary>
        /// 回転ペナルティ剛性 (CapNode-PileNode 等の回転拘束用) [kN·m/rad]。
        /// 2026-05-06: 実杭の回転剛性 (1e7 オーダー) の 10× 余裕。
        /// </summary>
        public const double KbigRotation = 1e8;

        /// <summary>
        /// 鉛直杭ばねの初期剛性フォールバック値 [kN/m]。
        /// VerticalPileSpringCurve でカーブから計算できない場合のデフォルト。
        /// </summary>
        public const double DefaultVerticalPileStiffness = 1e6;
    }
}

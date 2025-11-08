namespace PileDesign.FEM
{
    ///// <summary>
    ///// 解析モデルでは、kN, m単位系
    ///// </summary>
    //public class Material
    //{
    //    public double E { get; }
    //    public double G { get; }
    //    public double P { get; }

    //    // コンストラクタ
    //    public Material(double youngsModulus, double poissonRatio)
    //    {
    //        E = youngsModulus;
    //        G = E / (2 * (1 + poissonRatio));
    //        P = poissonRatio;
    //    }
    //}
    /// <summary>
    /// 解析モデルでは、kN, m単位系
    /// </summary>
    public class Material
    {
        public double E { get; set; }
        public double G { get; set; }
        public double P { get; set; }

        // パラメータなしコンストラクタ（必須）
        public Material() { }

        // 生成用コンストラクタ
        public Material(double youngsModulus, double poissonRatio)
        {
            E = youngsModulus;
            G = E / (2 * (1 + poissonRatio));
            P = poissonRatio;
        }
    }
}
namespace PileDesign.FEM
{
    //public class Section(Material material, double ax, double ay, double az, double ix, double iy, double iz)
    //{
    //    public Material Material { get; } = material;
    //    public double AX { get; } = ax;
    //    public double AY { get; } = ay;
    //    public double AZ { get; } = az;
    //    public double IX { get; } = ix;
    //    public double IY { get; } = iy;
    //    public double IZ { get; } = iz;

    //    public Section DeepCopy()
    //    {
    //        // MaterialにDeepCopyがあれば使う。なければ参照コピー。
    //        var materialCopy = Material is not null && Material.GetType().GetMethod("DeepCopy") is not null
    //            ? (Material)Material.GetType().GetMethod("DeepCopy")!.Invoke(Material, null)
    //            : Material;

    //        return new Section(materialCopy, AX, AY, AZ, IX, IY, IZ);
    //    }
    //}
    public class Section
    {
        public Material Material { get; set; }
        public double AX { get; set; }
        public double AY { get; set; }
        public double AZ { get; set; }
        public double IX { get; set; }
        public double IY { get; set; }
        public double IZ { get; set; }

        // パラメータなしコンストラクタ（必須）
        public Section() { }

        // 生成用コンストラクタ
        public Section(Material material, double ax, double ay, double az, double ix, double iy, double iz)
        {
            Material = material;
            AX = ax;
            AY = ay;
            AZ = az;
            IX = ix;
            IY = iy;
            IZ = iz;
        }

        public Section DeepCopy()
        {
            // 材料は共有でよい (値だけを持ち、解析中に書き換えない)。
            //
            // 以前はここで DeepCopy という名前のメソッドを反射で探していたが、
            // Material にも派生 6 型のどれにも存在せず、常に空振りして else 側へ落ちていた。
            // 梁 1 本を複製するたびに探索を 2 回払っていただけ。
            // 材料を複製したくなったら、反射ではなく型に仮想メソッドを足すこと。
            var materialCopy = Material;

            return new Section(materialCopy, AX, AY, AZ, IX, IY, IZ);
        }
    }
}
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
            var materialCopy = Material is not null && Material.GetType().GetMethod("DeepCopy") is not null
                ? (Material)Material.GetType().GetMethod("DeepCopy")!.Invoke(Material, null)
                : Material;

            return new Section(materialCopy, AX, AY, AZ, IX, IY, IZ);
        }
    }
}
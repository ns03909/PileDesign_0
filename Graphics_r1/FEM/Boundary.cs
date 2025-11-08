namespace PileDesign.FEM
{
    // 拘束条件
    public class Boundary(bool ux, bool uy, bool uz, bool rx, bool ry, bool rz)
    {
        public bool Ux { get; set; } = ux;
        public bool Uy { get; set; } = uy;
        public bool Uz { get; set; } = uz;
        public bool Rx { get; set; } = rx;
        public bool Ry { get; set; } = ry;
        public bool Rz { get; set; } = rz;

        public void Set(bool ux, bool uy, bool uz, bool rx, bool ry, bool rz)
        {
            Ux = ux;
            Uy = uy;
            Uz = uz;
            Rx = rx;
            Ry = ry;
            Rz = rz;
        }
    }
}
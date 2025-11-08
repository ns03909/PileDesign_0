namespace PileDesignCore
{
    public enum BoundaryType
    {
        Free,
        Fix
    }

    public class Boundary
    {
        public BoundaryType Ux { get; }
        public BoundaryType Uy { get; }
        public BoundaryType Uz { get; }
        public BoundaryType Tx { get; }
        public BoundaryType Ty { get; }
        public BoundaryType Tz { get; }

        public Boundary(BoundaryType ux, BoundaryType uy, BoundaryType uz, BoundaryType tx, BoundaryType ty, BoundaryType tz)
        {
            Ux = ux;
            Uy = uy;
            Uz = uz;
            Tx = tx;
            Ty = ty;
            Tz = tz;
        }
    }
}
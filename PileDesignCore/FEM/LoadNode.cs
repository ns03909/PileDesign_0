namespace PileDesignCore
{
    internal class LoadNode
    {
        public float LoadUX { get; }
        public float LoadUY { get; }
        public float LoadUZ { get; }
        public float LoadTX { get; }
        public float LoadTY { get; }
        public float LoadTZ { get; }

        public LoadNode(float loadUX, float loadUY, float loadUZ, float loadTX, float loadTY, float loadTZ)
        {
            LoadUX = loadUX;
            LoadUY = loadUY;
            LoadUZ = loadUZ;
            LoadTX = loadTX;
            LoadTY = loadTY;
            LoadTZ = loadTZ;
        }
    }
}
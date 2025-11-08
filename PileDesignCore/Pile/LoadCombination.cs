namespace PileDesignCore.Pile
{
    internal class LoadCombination
    {
        public int ID { get; set; }
        public double Alpha1 { get; set; }
        public double Beta1 { get; set; }
        public double Beta2 { get; set; }

        public LoadCombination(float gamma, int j)
        {
            if (gamma == 1.0f)
            {
                ID = 0;
                Alpha1 = Beta1 = Beta2 = 1.0;
            }
            else
            {
                ID = j;
                if (j == 0)
                {
                    Alpha1 = Beta1 = gamma;
                    Beta2 = -1.0;
                }
                else if (j == 1)
                {
                    Alpha1 = Beta1 = 1.0;
                    Beta2 = -gamma;
                }
                else if (j == 2)
                {
                    Alpha1 = Beta1 = gamma;
                    Beta2 = 1.0;
                }
                else if (j == 3)
                {
                    Alpha1 = Beta1 = 1.0;
                    Beta2 = gamma;
                }
            }
        }
    }
}


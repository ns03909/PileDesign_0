namespace PileDesign.Services
{

    public static class ModifyString
    {
        public static string AddSign(double a, string formatSpecifier)
        {
            string sign = a > 0 ? "+" : a == 0 ? "±" : "";
            return string.Format("{0}{1:" + formatSpecifier + "}", sign, a);
        }

    }
}

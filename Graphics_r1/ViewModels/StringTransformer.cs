namespace PileDesign.ViewModels
{
    public class StringTransformer
    {
        public static string TransformLastCharacter(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            char lastChar = input[^1];
            string prefix = input[..^1];

            if (char.IsDigit(lastChar))
            {
                if (lastChar < '9')
                    return prefix + (char)(lastChar + 1);
                else
                    return prefix + "10";
            }
            if (char.IsLower(lastChar))
            {
                if (lastChar < 'z')
                    return prefix + (char)(lastChar + 1);
                else
                    return prefix + "aa";
            }
            if (char.IsUpper(lastChar))
            {
                if (lastChar < 'Z')
                    return prefix + (char)(lastChar + 1);
                else
                    return prefix + "AA";
            }
            return input;
        }
    }
}
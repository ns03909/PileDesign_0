//namespace PileDesign.ViewModels
//{
//    public class StringTransformer
//    {
//        public static string TransformLastCharacter(string input)
//        {
//            if (string.IsNullOrEmpty(input))
//            {
//                return input;
//            }

//            char lastChar = input[^1]; // 最後の文字を取得
//            string newString;

//            if (lastChar >= '0' && lastChar <= '8') // 0-8 の場合
//            {
//                newString = input[..^1] + (char)(lastChar + 1);
//            }
//            else if (lastChar == '9') // 9 の場合
//            {
//                newString = input[..^1] + "10";
//            }
//            else if (lastChar >= 'a' && lastChar <= 'y') // a-y の場合
//            {
//                newString = input[..^1] + (char)(lastChar + 1);
//            }
//            else if (lastChar == 'z') // z の場合
//            {
//                newString = input[..^1] + "aa";
//            }
//            else if (lastChar >= 'A' && lastChar <= 'Y') // A-Y の場合
//            {
//                newString = input[..^1] + (char)(lastChar + 1);
//            }
//            else if (lastChar == 'Z') // Z の場合
//            {
//                newString = input[..^1] + "AA";
//            }
//            else
//            {
//                newString = input;
//            }

//            return newString;
//        }
//    }
//}
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
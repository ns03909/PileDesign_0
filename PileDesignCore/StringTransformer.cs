using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PileDesignCore
{
    using System;

    public class StringTransformer
    {
        public static string TransformLastCharacter(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            char lastChar = input[input.Length - 1]; // 最後の文字を取得
            string newString;

            if (lastChar >= '0' && lastChar <= '8') // 0-8 の場合
            {
                newString = input.Substring(0, input.Length - 1) + (char)(lastChar + 1);
            }
            else if (lastChar == '9') // 9 の場合
            {
                newString = input.Substring(0, input.Length - 1) + "10";
            }
            else if (lastChar >= 'a' && lastChar <= 'y') // a-y の場合
            {
                newString = input.Substring(0, input.Length - 1) + (char)(lastChar + 1);
            }
            else if (lastChar == 'z') // z の場合
            {
                newString = input.Substring(0, input.Length - 1) + "aa";
            }
            else if (lastChar >= 'A' && lastChar <= 'Y') // A-Y の場合
            {
                newString = input.Substring(0, input.Length - 1) + (char)(lastChar + 1);
            }
            else if (lastChar == 'Z') // Z の場合
            {
                newString = input.Substring(0, input.Length - 1) + "AA";
            }
            else
            {
                newString = input;
            }

            return newString;
        }

        //public static void Main(string[] args)
        //{
        //    // テスト
        //    Console.WriteLine(TransformLastCharacter("Hello1"));  // Output: Hello2
        //    Console.WriteLine(TransformLastCharacter("abcx"));    // Output: abcy
        //    Console.WriteLine(TransformLastCharacter("HappyZ"));  // Output: HappyAA
        //    Console.WriteLine(TransformLastCharacter("test9"));   // Output: test10
        //    Console.WriteLine(TransformLastCharacter("z"));       // Output: aa
        //    Console.WriteLine(TransformLastCharacter("Z"));       // Output: AA
        //    Console.WriteLine(TransformLastCharacter(""));        // Output: (空文字)
        //}
    }
}

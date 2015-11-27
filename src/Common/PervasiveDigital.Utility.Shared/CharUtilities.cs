using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Utilities
{
    public static class CharUtilities
    {
        public static bool IsDigit(char ch)
        {
            return (ch >= '0' && ch <= '9');
        }

        public static bool IsLetter(char ch)
        {
            return (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');
        }

        public static bool IsLetterOrDigit(char ch)
        {
            return IsDigit(ch) || IsLetter(ch);
        }

        public static char ToUpper(char ch)
        {
            if (ch >= 'a' && ch <= 'z')
                ch = (char)(ch - 32);
            return ch;
        }
    }
}

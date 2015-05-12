using System;
using Microsoft.SPOT;

namespace PervasiveDigital.Utilities
{
    public static class StringUtilities
    {
        public static bool IsNullOrEmpty(string s)
        {
            return s == null || s.Length == 0;
        }

        public static int Compare(string left, int idxLeft, string right, int idxRight, int length)
        {
            throw new NotImplementedException();
        }
    }
}

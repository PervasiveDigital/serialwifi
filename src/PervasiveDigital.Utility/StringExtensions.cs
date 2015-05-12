using System.Text;

namespace PervasiveDigital.Utilities
{
    public static class StringExtensions
    {
        public static string Replace(this string content, char find, char replace)
        {
            StringBuilder result = new StringBuilder();

            var len = content.Length;
            for (var i = 0 ; i<len ; ++i)
            {
                if (content[i] == find)
                    result.Append(replace);
                else
                    result.Append(content[i]);
            }
            return result.ToString();
        }

        public static string Replace(this string content, string find, string replace)
        {
            int startFrom = 0;
            int findItemLength = find.Length;

            int firstFound = content.IndexOf(find, startFrom);
            StringBuilder returning = new StringBuilder();

            string workingString = content;

            while ((firstFound = workingString.IndexOf(find, startFrom)) >= 0)
            {
                returning.Append(workingString.Substring(0, firstFound));
                returning.Append(replace);

                // the remaining part of the string.
                workingString = workingString.Substring(firstFound + findItemLength, workingString.Length - (firstFound + findItemLength));
            }

            returning.Append(workingString);

            return returning.ToString();
        }

        public static bool StartsWith(this string source, string search)
        {
            if (source == null)
                return false;
            if (search.Length > source.Length)
                return false;
            for (var i = 0; i < search.Length; ++i)
            {
                if (source[i] != search[i])
                    return false;
            }
            return true;
        }

        public static bool EndsWith(this string source, string search)
        {
            if (source == null)
                return false;
            if (search.Length > source.Length)
                return false;

            var iSrc = source.Length-1;            
            for (var i = search.Length-1; i >= 0 ; --i)
            {
                if (source[iSrc] != search[i])
                    return false;
            }
            return true;
        }
    }
}

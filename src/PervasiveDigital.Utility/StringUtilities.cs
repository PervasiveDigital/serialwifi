// The Format routines used below are (c) Ross McDermott and used under this license:
// Copyright (c) 2010 Ross McDermott
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation 
// files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, 
// modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE 
// WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR 
// COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, 
// ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE. 
using System;
using System.Text;
using Microsoft.SPOT;

namespace PervasiveDigital.Utilities
{
    public static class StringUtilities
    {
        // This avoids the exception that can occur with a simple GetChars, though at the cost of some heap churn
        public static String ConvertToString(Byte[] byteArray)
        {
            var _chars = new char[byteArray.Length];
            bool _completed;
            int _bytesUsed, _charsUsed;
            Encoding.UTF8.GetDecoder().Convert(byteArray, 0, byteArray.Length, _chars, 0, byteArray.Length, false, out _bytesUsed, out _charsUsed, out _completed);
            return new string(_chars, 0, _charsUsed);
        }

        public static bool IsNullOrEmpty(string s)
        {
            return s == null || s.Length == 0;
        }

        public static int Compare(string left, int idxLeft, string right, int idxRight, int length)
        {
            throw new NotImplementedException();
        }

        public static string Format(string format, object arg)
        {
            return Format(format, new object[] { arg });
        }

        public static string Format(string format, params object[] args)
        {
            if (format == null)
                throw new ArgumentNullException("format");

            if (args == null)
                throw new ArgumentNullException("args");

            // Validate the structure of the format string.
            ValidateFormatString(format);

            StringBuilder bld = new StringBuilder();

            int endOfLastMatch = 0;
            int starting = 0;

            while (starting >= 0)
            {
                starting = format.IndexOf('{', starting);

                if (starting >= 0)
                {
                    if (starting != format.Length - 1)
                    {
                        if (format[starting + 1] == '{')
                        {
                            // escaped starting bracket.
                            starting = starting + 2;
                            continue;
                        }
                        else
                        {
                            bool found = false;
                            int endsearch = format.IndexOf('}', starting);

                            while (endsearch > starting)
                            {
                                if (endsearch != (format.Length - 1) && format[endsearch + 1] == '}')
                                {
                                    // escaped ending bracket
                                    endsearch = endsearch + 2;
                                }
                                else
                                {
                                    if (starting != endOfLastMatch)
                                    {
                                        string t = format.Substring(endOfLastMatch, starting - endOfLastMatch);
                                        t = t.Replace("{{", "{"); // get rid of the escaped brace
                                        t = t.Replace("}}", "}"); // get rid of the escaped brace
                                        bld.Append(t);
                                    }

                                    // we have a winner
                                    string fmt = format.Substring(starting, endsearch - starting + 1);

                                    if (fmt.Length >= 3)
                                    {
                                        fmt = fmt.Substring(1, fmt.Length - 2);

                                        string[] indexFormat = fmt.Split(new char[] { ':' });

                                        string formatString = string.Empty;

                                        if (indexFormat.Length == 2)
                                        {
                                            formatString = indexFormat[1];
                                        }


                                        // no format, just number
                                        int index = int.Parse(indexFormat[0]);
                                        bld.Append(FormatParameter(args[index], formatString));
                                    }

                                    endOfLastMatch = endsearch + 1;

                                    found = true;
                                    starting = endsearch + 1;
                                    break;
                                }


                                endsearch = format.IndexOf('}', endsearch);
                            }
                            // need to find the ending point

                            if (!found)
                            {
                                throw new FormatException(FormatException.ERROR_MESSAGE);
                            }
                        }
                    }
                    else
                    {
                        // invalid
                        throw new FormatException(FormatException.ERROR_MESSAGE);
                    }

                }

            }

            // copy any additional remaining part of the format string.
            if (endOfLastMatch != format.Length)
            {
                bld.Append(format.Substring(endOfLastMatch, format.Length - endOfLastMatch));
            }

            return bld.ToString();
        }

        private static void ValidateFormatString(string format)
        {
            char expected = '{';

            int i = 0;

            while ((i = format.IndexOfAny(new char[] { '{', '}' }, i)) >= 0)
            {
                if (i < (format.Length - 1) && format[i] == format[i + 1])
                {
                    // escaped brace. continue looking.
                    i = i + 2;
                    continue;
                }
                else if (format[i] != expected)
                {
                    // badly formed string.
                    throw new FormatException(FormatException.ERROR_MESSAGE);
                }
                else
                {
                    // move it along.
                    i++;

                    // expected it.
                    if (expected == '{')
                        expected = '}';
                    else
                        expected = '{';
                }
            }

            if (expected == '}')
            {
                // orpaned opening brace. Bad format.
                throw new FormatException(FormatException.ERROR_MESSAGE);
            }

        }

        private static string FormatParameter(object p, string formatString)
        {
            if (formatString == string.Empty)
                return p.ToString();

            if (p as IFormattable != null)
            {
                return ((IFormattable)p).ToString(formatString, null);
            }
            else if (p is DateTime)
            {
                return ((DateTime)p).ToString(formatString);
            }
            else if (p is Double)
            {
                return ((Double)p).ToString(formatString);
            }
            else if (p is Int16)
            {
                return ((Int16)p).ToString(formatString);
            }
            else if (p is Int32)
            {
                return ((Int32)p).ToString(formatString);
            }
            else if (p is Int64)
            {
                return ((Int64)p).ToString(formatString);
            }
            else if (p is SByte)
            {
                return ((SByte)p).ToString(formatString);
            }
            else if (p is Single)
            {
                return ((Single)p).ToString(formatString);
            }
            else if (p is UInt16)
            {
                return ((UInt16)p).ToString(formatString);
            }
            else if (p is UInt32)
            {
                return ((UInt32)p).ToString(formatString);
            }
            else if (p is UInt64)
            {
                return ((UInt64)p).ToString(formatString);
            }
            else
            {
                return p.ToString();
            }
        }

    }
}

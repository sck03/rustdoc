using System.Text;
using System.Text.RegularExpressions;

namespace ExportDocManager.Utils
{
    public static class NumberHelper
    {
        /// <summary>
        /// 解析十进制数值，如果解析失败返回0
        /// </summary>
        /// <param name="text">要解析的文本</param>
        /// <returns>解析后的十进制数值</returns>
        public static decimal ParseDecimal(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (decimal.TryParse(text, out decimal result))
            {
                return result;
            }
            return 0;
        }

        /// <summary>
        /// 解析整数，如果解析失败返回0
        /// </summary>
        public static int ParseInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            if (int.TryParse(text, out int result))
            {
                return result;
            }
            return 0;
        }

        private static readonly string[] Ones =
        {
            "", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE",
            "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN",
            "SEVENTEEN", "EIGHTEEN", "NINETEEN"
        };

        private static readonly string[] Tens =
        {
            "", "", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY"
        };

        public static string ToEnglishWords(decimal number)
        {
            if (number < 0) return "MINUS " + ToEnglishWords(Math.Abs(number));
            if (number == 0) return "ZERO";

            long intPart = (long)number;
            int decimalPart = (int)((number - intPart) * 100);

            string words = ConvertWholeNumber(intPart);

            if (decimalPart > 0)
            {
                words += " AND CENTS " + ConvertWholeNumber(decimalPart);
            }

            return words.Trim();
        }

        private static string ConvertWholeNumber(long number)
        {
            if (number == 0) return "";

            if (number < 20)
                return Ones[number];

            if (number < 100)
                return Tens[number / 10] + (number % 10 > 0 ? "-" + Ones[number % 10] : "");

            if (number < 1000)
                return Ones[number / 100] + " HUNDRED" + (number % 100 > 0 ? " AND " + ConvertWholeNumber(number % 100) : "");

            if (number < 1000000)
                return ConvertWholeNumber(number / 1000) + " THOUSAND" + (number % 1000 > 0 ? " " + ConvertWholeNumber(number % 1000) : "");

            if (number < 1000000000)
                return ConvertWholeNumber(number / 1000000) + " MILLION" + (number % 1000000 > 0 ? " " + ConvertWholeNumber(number % 1000000) : "");

            return ConvertWholeNumber(number / 1000000000) + " BILLION" + (number % 1000000000 > 0 ? " " + ConvertWholeNumber(number % 1000000000) : "");
        }

        /// <summary>
        /// 将金额转换为中文大写
        /// </summary>
        public static string ToChineseMoney(decimal money)
        {
            if (money == 0) return "零元整";

            string s = money.ToString("#L#E#D#C#K#E#D#C#J#E#D#C#I#E#D#C#H#E#D#C#G#E#D#C#F#E#D#C#.00");
            string d = Regex.Replace(s, @"((?<=-|^)[^1-9]*)|((?'z'0)[0A-E]*((?=[1-9])|(?'-z'(?=[F-L\.]|$))))|((?'b'[F-L])(?'z'0)[0A-E]*((?=[1-9])|(?'-z'(?=[[\.]|$))))", "${b}${z}");
            string result = Regex.Replace(d, ".", m =>
            {
                if (".".Equals(m.Value)) return "元";
                if ("0".Equals(m.Value)) return "零";
                if (m.Value.CompareTo("9") > 0)
                {
                    switch (m.Value)
                    {
                        case "A": return "拾";
                        case "B": return "佰";
                        case "C": return "仟";
                        case "D": return "万";
                        case "E": return "拾";
                        case "F": return "佰";
                        case "G": return "仟";
                        case "H": return "亿";
                        default: return "";
                    }
                }
                return ChineseDigits[int.Parse(m.Value)];
            });

            return ConvertToChineseMoneySimple(money);
        }

        private static readonly string[] ChineseDigits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };

        private static string ConvertToChineseMoneySimple(decimal number)
        {
            if (number == 0) return "零元整";

            StringBuilder sb = new StringBuilder();

            long integral = (long)number;
            int decimalPart = (int)((number - integral) * 100);

            if (integral > 0)
            {
                sb.Append(ConvertIntegralToChinese(integral) + "元");
            }

            if (decimalPart == 0)
            {
                sb.Append("整");
            }
            else
            {
                int jiao = decimalPart / 10;
                int fen = decimalPart % 10;

                if (integral == 0 && jiao == 0)
                {
                    // No leading unit before fen.
                }
                else if (jiao == 0 && integral > 0)
                {
                    sb.Append("零");
                }
                else if (jiao > 0)
                {
                    sb.Append(ChineseDigits[jiao] + "角");
                }

                if (fen > 0)
                {
                    sb.Append(ChineseDigits[fen] + "分");
                }
            }

            return sb.ToString();
        }

        private static string ConvertIntegralToChinese(long number)
        {
            if (number == 0) return "零";

            string[] units = { "", "拾", "佰", "仟" };
            string[] bigUnits = { "", "万", "亿", "兆" };

            string strNum = number.ToString();
            int len = strNum.Length;

            StringBuilder sb = new StringBuilder();

            int groupIndex = 0;
            int zeroCount = 0;

            for (int i = len; i > 0; i -= 4)
            {
                int start = Math.Max(0, i - 4);
                int length = i - start;
                string group = strNum.Substring(start, length);

                string groupStr = ConvertGroup(group, units);

                if (groupStr != "")
                {
                    sb.Insert(0, groupStr + bigUnits[groupIndex]);
                    zeroCount = 0;
                }
                else if (zeroCount == 0 && sb.Length > 0 && !sb.ToString().StartsWith("零"))
                {
                    sb.Insert(0, "零");
                    zeroCount++;
                }

                groupIndex++;
            }

            return sb.ToString().TrimStart('零');
        }

        private static string ConvertGroup(string group, string[] units)
        {
            StringBuilder sb = new StringBuilder();
            bool zeroFlag = false;

            for (int i = 0; i < group.Length; i++)
            {
                int digit = group[i] - '0';
                int unitIdx = group.Length - 1 - i;

                if (digit == 0)
                {
                    zeroFlag = true;
                }
                else
                {
                    if (zeroFlag)
                    {
                        sb.Append("零");
                        zeroFlag = false;
                    }
                    sb.Append(ChineseDigits[digit] + units[unitIdx]);
                }
            }

            return sb.ToString();
        }
    }
}

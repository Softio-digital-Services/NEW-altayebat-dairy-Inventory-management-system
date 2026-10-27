using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using InventorySystem.Helpers;

namespace InventorySystem.Services
{
    public class BarcodeService
    {
        private const int MAX_SKU_LENGTH = 15;

        /// <summary>
        /// Generates a unique SKU based on Category and Name.
        /// Logic: CAT(3)-NAM(3)-SEQ(3)
        /// Example: ELE-SON-001
        /// </summary>
        public string GenerateSKU(string category, string name)
        {
            string catPart = CleanString(category, 3);
            string namePart = CleanString(name, 3);

            // If name is too short, take more from category or vice versa to fill space reasonably
            if (string.IsNullOrEmpty(catPart) && string.IsNullOrEmpty(namePart))
                catPart = "ITM";

            string baseSku = "";
            if (!string.IsNullOrEmpty(catPart)) baseSku += catPart;
            if (!string.IsNullOrEmpty(namePart)) baseSku += (baseSku.Length > 0 ? "-" : "") + namePart;

            // Enforce character limit early
            if (baseSku.Length > MAX_SKU_LENGTH - 4) // Leave room for suffix
                baseSku = baseSku.Substring(0, MAX_SKU_LENGTH - 4);

            return ResolveUniqueSKU(baseSku);
        }

        private string CleanString(string input, int length)
        {
            if (string.IsNullOrWhiteSpace(input) || input.ToLower() == "null") return "";
            
            // Remove special characters and spaces
            string clean = Regex.Replace(input.ToUpper(), @"[^A-Z0-9]", "");
            return clean.Length > length ? clean.Substring(0, length) : clean;
        }

        private string ResolveUniqueSKU(string baseSku)
        {
            int counter = 1;
            string currentSku = $"{baseSku}-{counter:D3}";

            while (SKUExists(currentSku))
            {
                counter++;
                currentSku = $"{baseSku}-{counter:D3}";
                
                // Safety break if we somehow hit 999 duplicates
                if (counter > 999) break; 
            }

            return currentSku;
        }

        public bool SKUExists(string sku)
        {
            string sql = "SELECT COUNT(*) FROM parts WHERE part_number = @sku AND date_deleted IS NULL";
            int count = DatabaseHelper.ExecuteScalar<int>(sql, new SqliteParameter("@sku", sku));
            return count > 0;
        }

        // Code 128 bar/space widths for symbol values 0..106 (106 = stop, 7 elements)
        private static readonly string[] Code128Patterns =
        {
            "212222","222122","222221","121223","121322","131222","122213","122312","132212","221213",
            "221312","231212","112232","122132","122231","113222","123122","123221","223211","221132",
            "221231","213212","223112","312131","311222","321122","321221","312212","322112","322211",
            "212123","212321","232121","111323","131123","131321","112313","132113","132311","211313",
            "231113","231311","112133","112331","132131","113123","113321","133121","313121","211331",
            "231131","213113","213311","213131","311123","311321","331121","312113","312311","332111",
            "314111","221411","431111","111224","111422","121124","121421","141122","141221","112214",
            "112412","122114","122411","142112","142211","241211","221114","413111","241112","134111",
            "111242","121142","121241","114212","124112","124211","411212","421112","421211","212141",
            "214121","412121","111143","111341","131141","114113","114311","411113","411311","113141",
            "114131","311141","411131","211412","211214","211232","2331112"
        };

        /// <summary>
        /// Encodes text as Code 128 and returns the bar/space module widths, starting with a bar
        /// (no quiet zones). Digit-only codes use set C (half as wide); everything else set B.
        /// Characters outside printable ASCII become '?'.
        /// </summary>
        public static int[] EncodeCode128(string text)
        {
            text ??= "";
            var values = new List<int>();
            bool allDigits = text.Length >= 2 && text.All(char.IsAsciiDigit);
            if (allDigits)
            {
                values.Add(105); // Start C
                int pairs = text.Length / 2;
                for (int i = 0; i < pairs; i++)
                    values.Add((text[i * 2] - '0') * 10 + (text[i * 2 + 1] - '0'));
                if (text.Length % 2 == 1)
                {
                    values.Add(100); // Code B for the last digit
                    values.Add(text[text.Length - 1] - 32);
                }
            }
            else
            {
                values.Add(104); // Start B
                foreach (char ch in text)
                {
                    char c = ch >= 32 && ch <= 126 ? ch : '?';
                    values.Add(c - 32);
                }
            }

            int sum = values[0];
            for (int i = 1; i < values.Count; i++) sum += values[i] * i;
            values.Add(sum % 103);
            values.Add(106); // Stop

            var modules = new List<int>();
            foreach (int v in values)
                foreach (char w in Code128Patterns[v]) modules.Add(w - '0');
            return modules.ToArray();
        }

        /// <summary>Total width in modules of the Code 128 symbol (without quiet zones).</summary>
        public static int Code128ModuleCount(string text) => EncodeCode128(text).Sum();

        /// <summary>
        /// Draws a scannable Code 128 barcode with every bar a whole number of pixels wide
        /// (moduleWidth pixels per module), starting at x. Graphics must be in pixel units.
        /// </summary>
        public static void DrawCode128(Graphics g, string text, int x, int y, int moduleWidth, int height)
        {
            bool bar = true;
            foreach (int w in EncodeCode128(text))
            {
                int px = w * moduleWidth;
                if (bar) g.FillRectangle(Brushes.Black, x, y, px, height);
                x += px;
                bar = !bar;
            }
        }

        /// <summary>
        /// Real Code 128 barcode as a bitmap of the given size: bars are scaled to whole pixels,
        /// centred, with white quiet zones.
        /// </summary>
        public Bitmap RenderCode128(string text, int width = 300, int height = 100)
        {
            if (string.IsNullOrEmpty(text)) return new Bitmap(1, 1);
            width = Math.Max(1, width);
            height = Math.Max(1, height);
            int modules = Code128ModuleCount(text);
            int moduleWidth = Math.Max(1, width / (modules + 20));
            var bmp = new Bitmap(Math.Max(width, (modules + 20) * moduleWidth), height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                int x = (bmp.Width - modules * moduleWidth) / 2;
                DrawCode128(g, text, x, 0, moduleWidth, height);
            }
            return bmp;
        }
    }
}

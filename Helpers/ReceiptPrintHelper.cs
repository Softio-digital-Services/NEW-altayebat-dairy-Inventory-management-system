using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace InventorySystem.Helpers
{
    public class ReceiptPrintItem
    {
        public string Name { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get; set; }
    }

    public class ReceiptPrintOptions
    {
        public string PrinterName { get; set; }
        public int Copies { get; set; } = 1;
        public bool Landscape { get; set; }
        public bool Color { get; set; } = true;
        public string CustomerName { get; set; }
        public string ShippingTo { get; set; }
        public string CurrencySymbol { get; set; } = "$";
        public List<ReceiptPrintItem> Items { get; set; } = new List<ReceiptPrintItem>();
        public decimal Subtotal { get; set; }
        public decimal Vat { get; set; }
        public decimal Shipping { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
        /// <summary>Receipt paper width in mm. 0 = use saved default.</summary>
        public double PaperWidthMm { get; set; }
        /// <summary>Receipt paper height in mm. 0 = auto tall roll.</summary>
        public double PaperHeightMm { get; set; }
        public double MarginMm { get; set; } = -1;
        public double MarginTopMm { get; set; } = -1;
        public double MarginRightMm { get; set; } = -1;
        public double MarginBottomMm { get; set; } = -1;
        public double MarginLeftMm { get; set; } = -1;
    }

    public static class ReceiptPrintHelper
    {
        private static ReceiptPrintOptions _opts;
        private static int _nextItem;

        public static void Print(ReceiptPrintOptions options)
        {
            if (options == null) return;
            _opts = options;

            var defaults = PrintSettings.GetSnapshot();
            var resolved = PrintSettings.ResolveForPrinter(options.PrinterName, "receipt", defaults);
            double widthMm = options.PaperWidthMm > 0 ? options.PaperWidthMm : resolved.WidthMm;
            double heightMm = options.PaperHeightMm > 0 ? options.PaperHeightMm
                : (resolved.HeightMm > 0 ? resolved.HeightMm : 0);
            double uni = options.MarginMm >= 0 ? options.MarginMm
                : (resolved.MarginMm >= 0 ? resolved.MarginMm : defaults.ReceiptMarginMm);
            double mTop = options.MarginTopMm >= 0 ? options.MarginTopMm
                : (resolved.MarginTopMm >= 0 ? resolved.MarginTopMm : uni);
            double mRight = options.MarginRightMm >= 0 ? options.MarginRightMm
                : (resolved.MarginRightMm >= 0 ? resolved.MarginRightMm : uni);
            double mBottom = options.MarginBottomMm >= 0 ? options.MarginBottomMm
                : (resolved.MarginBottomMm >= 0 ? resolved.MarginBottomMm : uni);
            double mLeft = options.MarginLeftMm >= 0 ? options.MarginLeftMm
                : (resolved.MarginLeftMm >= 0 ? resolved.MarginLeftMm : uni);
            string printer = options.PrinterName;
            if (string.IsNullOrWhiteSpace(printer))
            {
                try { printer = new PrinterSettings().PrinterName; } catch { }
            }
            string mode = PrintSettings.NormalizeReceiptMode(defaults.ReceiptMode);
            bool direct = mode == "direct" || (mode == "auto" && RawPrinterHelper.UsesTsplLabelDriver(printer));
            if (direct && !string.IsNullOrWhiteSpace(printer))
            {
                PrintDirect(options, printer, widthMm, mLeft, mRight, mTop, mBottom);
                return;
            }

            int paperW = Math.Max(1, PrintSettings.MmToHundredths(widthMm));
            int paperH = heightMm > 0
                ? Math.Max(1, PrintSettings.MmToHundredths(heightMm))
                : Math.Max(1, PrintSettings.MmToHundredths(Math.Max(200, widthMm * 3.5)));

            using (PrintDocument pd = new PrintDocument())
            {
                try { pd.DefaultPageSettings.PaperSize = new PaperSize("Receipt", paperW, paperH); }
                catch { pd.DefaultPageSettings.PaperSize = new PaperSize("Receipt", paperW, paperH); }
                pd.DefaultPageSettings.Margins = new Margins(
                    PrintSettings.MmToHundredths(mLeft),
                    PrintSettings.MmToHundredths(mRight),
                    PrintSettings.MmToHundredths(mTop),
                    PrintSettings.MmToHundredths(mBottom));
                pd.DefaultPageSettings.Landscape = options.Landscape;
                pd.DefaultPageSettings.Color = options.Color;

                if (!string.IsNullOrWhiteSpace(options.PrinterName))
                {
                    try { pd.PrinterSettings.PrinterName = options.PrinterName; } catch { }
                }
                try
                {
                    pd.PrinterSettings.Copies = (short)Math.Max(1, Math.Min(99, options.Copies));
                }
                catch { }

                try
                {
                    PrintSettings.SavePrinterProfile(options.PrinterName, "receipt", new PrinterJobProfile
                    {
                        WidthMm = widthMm,
                        HeightMm = heightMm,
                        MarginMm = uni,
                        MarginTopMm = mTop,
                        MarginRightMm = mRight,
                        MarginBottomMm = mBottom,
                        MarginLeftMm = mLeft,
                        PaperMode = "roll"
                    });
                }
                catch { }

                _nextItem = 0;
                pd.PrintPage += PrintPage;
                pd.Print();
            }
        }

        /// <summary>
        /// Draws the receipt ourselves at the printer's 203 dpi and sends it as an ESC/POS image, bypassing
        /// the Windows driver. The paper is fed exactly as long as the receipt.
        /// </summary>
        private static void PrintDirect(ReceiptPrintOptions o, string printer, double widthMm,
            double mLeft, double mRight, double mTop, double mBottom)
        {
            using var bmp = RenderDirectImage(o, widthMm, mLeft, mRight, mTop, mBottom);
            RawPrinterHelper.SendRaw(printer, RawPrinterHelper.BuildEscPosRaster(bmp, o.Copies), "Receipt");
        }

        /// <summary>The receipt as a 203 dpi image the width of the print head, cropped to its length.</summary>
        public static Bitmap RenderDirectImage(ReceiptPrintOptions o, double widthMm,
            double mLeft, double mRight, double mTop, double mBottom)
        {
            const float dpi = 203.2f;
            int dots = RawPrinterHelper.PrintableDots(widthMm);
            // Side margins are inside the printable area already; keep them small so text isn't squeezed
            int left = PrintSettings.MmToHundredths(Math.Min(mLeft, 3));
            int right = PrintSettings.MmToHundredths(Math.Min(mRight, 3));
            int top = PrintSettings.MmToHundredths(mTop);
            int pageW = (int)(dots / dpi * 100);
            var bounds = new Rectangle(left, top, Math.Max(40, pageW - left - right), int.MaxValue / 4);

            int bottom;
            using (var probe = new Bitmap(1, 1))
            {
                probe.SetResolution(dpi, dpi);
                using var pg = Graphics.FromImage(probe);
                Prepare(pg);
                int i = 0;
                bottom = DrawReceipt(pg, o, bounds, ref i, out _) + PrintSettings.MmToHundredths(mBottom);
            }

            int heightDots = Math.Max(8, (int)Math.Ceiling(bottom * dpi / 100));
            var bmp = new Bitmap(dots, heightDots, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                Prepare(g);
                int i = 0;
                DrawReceipt(g, o, bounds, ref i, out _);
            }
            return bmp;
        }

        /// <summary>Page units of 1/100 inch on a bitmap, same as a printer page.</summary>
        private static void Prepare(Graphics g)
        {
            g.PageUnit = GraphicsUnit.Document;
            g.PageScale = 3f;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        }

        private static void PrintPage(object sender, PrintPageEventArgs e)
        {
            var o = _opts ?? new ReceiptPrintOptions();
            DrawReceipt(e.Graphics, o, e.MarginBounds, ref _nextItem, out bool more);
            e.HasMorePages = more;
        }

        /// <summary>Draws the receipt into bounds (1/100 inch). Returns the y below the last line.</summary>
        private static int DrawReceipt(Graphics g, ReceiptPrintOptions o, Rectangle bounds, ref int nextItem, out bool more)
        {
            more = false;
            int w = Math.Max(40, bounds.Width);
            // Scale fonts slightly for narrow (58mm) vs wide (80mm+) rolls
            float scale = Math.Max(0.85f, Math.Min(1.15f, w / 280f));
            using Font fH = new Font("Segoe UI", 11f * scale, FontStyle.Bold);
            using Font fS = new Font("Segoe UI", 8.5f * scale);
            using Font fI = new Font("Consolas", 8f * scale);
            using Font fB = new Font("Segoe UI", 9.5f * scale, FontStyle.Bold);

            int m = bounds.Left;
            int y = bounds.Top;
            var center = new StringFormat { Alignment = StringAlignment.Center };
            var right = new StringFormat { Alignment = StringAlignment.Far };
            string sym = string.IsNullOrWhiteSpace(o.CurrencySymbol) ? "$" : o.CurrencySymbol;

            string company = ThemeConfig.CompanyName;
            if (string.IsNullOrWhiteSpace(company)) company = ThemeConfig.CompanyName;
            g.DrawString(company.ToUpperInvariant(), fH, Brushes.Black, new RectangleF(m, y, w, 22 * scale), center);
            y += (int)(24 * scale);
            g.DrawString("SALES RECEIPT", fS, Brushes.Black, new RectangleF(m, y, w, 18 * scale), center);
            y += (int)(18 * scale);
            g.DrawString(DateTime.Now.ToString("g"), fS, Brushes.Black, new RectangleF(m, y, w, 18 * scale), center);
            y += (int)(22 * scale);
            g.DrawLine(Pens.Black, m, y, m + w, y);
            y += 8;

            if (!string.IsNullOrWhiteSpace(o.CustomerName))
            {
                g.DrawString("Customer: " + o.CustomerName, fS, Brushes.Black, m, y);
                y += (int)(18 * scale);
            }
            if (!string.IsNullOrWhiteSpace(o.ShippingTo))
            {
                g.DrawString("Ship to: " + o.ShippingTo.Replace("\r\n", ", ").Replace("\n", ", "), fS, Brushes.Black,
                    new RectangleF(m, y, w, 36 * scale));
                y += (int)(36 * scale);
            }

            g.DrawString("QTY", fI, Brushes.Black, m, y);
            g.DrawString("ITEM", fI, Brushes.Black, m + (int)(36 * scale), y);
            g.DrawString("TOTAL", fI, Brushes.Black, new RectangleF(m, y, w, 16 * scale), right);
            y += (int)(16 * scale);
            g.DrawLine(Pens.Black, m, y, m + w, y);
            y += 6;

            if (o.Items != null)
            {
                for (; nextItem < o.Items.Count; nextItem++)
                {
                    var it = o.Items[nextItem];
                    string name = it.Name ?? "";
                    string money = FormatMoney(sym, it.Total);
                    // Full name, wrapped onto extra lines between the quantity and the price
                    float nameX = m + 36 * scale;
                    float nameW = Math.Max(30, w - 36 * scale - g.MeasureString(money, fI).Width - 6 * scale);
                    float nameH = Math.Max(16 * scale, g.MeasureString(name, fI, (int)nameW).Height);
                    g.DrawString(Math.Max(1, it.Qty).ToString(), fI, Brushes.Black, m, y);
                    g.DrawString(name, fI, Brushes.Black, new RectangleF(nameX, y, nameW, nameH + 2));
                    g.DrawString(money, fI, Brushes.Black, new RectangleF(m, y, w, 16 * scale), right);
                    y += (int)Math.Ceiling(Math.Max(16 * scale, nameH + 2 * scale));
                    if (y > bounds.Bottom - 120 && nextItem < o.Items.Count - 1)
                    {
                        nextItem++;
                        more = true;
                        return y;
                    }
                }
            }

            y += 6;
            g.DrawLine(Pens.Black, m, y, m + w, y);
            y += 8;

            void Row(string label, decimal amount, bool bold = false)
            {
                var font = bold ? fB : fS;
                g.DrawString(label, font, Brushes.Black, m, y);
                g.DrawString(FormatMoney(sym, amount), font, Brushes.Black, new RectangleF(m, y, w, 18 * scale), right);
                y += (int)(18 * scale);
            }

            Row("Subtotal", o.Subtotal);
            if (o.Vat > 0.009m) Row("VAT", o.Vat);
            if (o.Shipping > 0.009m) Row("Shipping", o.Shipping);
            if (o.Discount > 0.009m) Row("Discount", -o.Discount);
            y += 4;
            g.DrawLine(Pens.Black, m, y, m + w, y);
            y += 8;
            Row("TOTAL", o.Total, true);
            y += (int)(16 * scale);
            g.DrawString("Thank you!", fS, Brushes.Black, new RectangleF(m, y, w, 18 * scale), center);
            y += (int)(18 * scale);
            return y;
        }

        private static string FormatMoney(string symbol, decimal amount)
        {
            return $"{symbol}{amount:0.00}";
        }
    }
}

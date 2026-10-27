using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Windows.Forms;
using InventorySystem.Services;

namespace InventorySystem.Helpers
{
    public class LabelPrintItem
    {
        public string Name { get; set; }
        public string SKU { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
    }

    public class LabelPrintOptions
    {
        public string PrinterName { get; set; }
        public int Copies { get; set; } = 1;
        public bool Landscape { get; set; }
        public bool Color { get; set; } = true;
        public string PageRange { get; set; } = "all";
        public double LabelWidthMm { get; set; }
        public double LabelHeightMm { get; set; }
        public double LabelGapMm { get; set; } = -1;
        public double MarginMm { get; set; } = -1;
        public double MarginTopMm { get; set; } = -1;
        public double MarginRightMm { get; set; } = -1;
        public double MarginBottomMm { get; set; } = -1;
        public double MarginLeftMm { get; set; } = -1;
        public int Columns { get; set; } = -1; // -1 = use saved / auto
        public string PaperMode { get; set; }
        public double PageWidthMm { get; set; }
        public double PageHeightMm { get; set; }
    }

    public class ThermalLabelHelper
    {
        private static List<LabelPrintItem> _printQueue;
        private static int _queueIndex;
        private static int _pageNumber;
        private static HashSet<int> _allowedPages;
        private static bool _grayscale;

        // Active layout for the current print job (hundredths of an inch)
        private static int _labelWidth;
        private static int _labelHeight;
        private static int _gapX;
        private static int _gapY;
        private static int _maxCols;
        private static bool _rollMode;
        // Resolved sizes in mm for the direct (TSPL) path
        private static double _wMm, _hMm, _gapMm, _mTopMm, _mRightMm, _mBottomMm, _mLeftMm;

        /// <summary>
        /// Opens a compact themed PrintPreviewDialog (legacy path).
        /// </summary>
        public static void GenerateLabelPDF(List<LabelPrintItem> items, IWin32Window owner = null)
        {
            if (items == null || items.Count == 0) return;

            PrintDocument pd = BuildDocument(items, new LabelPrintOptions());

            using (PrintPreviewDialog preview = new PrintPreviewDialog())
            {
                preview.Document = pd;
                preview.Text = LocalizationManager.GetString("ThermalLabel_PreviewTitle", "Label Preview");
                if (owner is Form ownerForm)
                    preview.Owner = ownerForm;

                ThemeConfig.ApplyPrintPreviewTheme(preview);

                if (owner != null)
                    preview.ShowDialog(owner);
                else
                    preview.ShowDialog();
            }
        }

        /// <summary>
        /// Prints labels directly using the chosen printer and options (no Chromium dialog).
        /// </summary>
        public static void PrintLabels(List<LabelPrintItem> items, LabelPrintOptions options, IWin32Window owner = null)
        {
            if (items == null || items.Count == 0) return;
            options ??= new LabelPrintOptions();

            PrintDocument pd = BuildDocument(items, options);
            try
            {
                string printer = pd.PrinterSettings.PrinterName;
                // TSC/Xprinter label drivers squeeze a whole page onto the label; send exact-size labels instead
                if (RawPrinterHelper.UsesTsplLabelDriver(printer)
                    && PrintSettings.NormalizeReceiptMode(PrintSettings.GetSnapshot().ReceiptMode) != "driver")
                {
                    PrintLabelsTspl(printer, Math.Max(1, Math.Min(99, options.Copies)));
                    return;
                }
                pd.Print();
            }
            finally
            {
                pd.Dispose();
            }
        }

        /// <summary>
        /// One label per sticker, drawn at the printer's 203 dpi and sent as TSPL (SIZE / GAP / BITMAP / PRINT).
        /// The printer's gap sensor lines every label up with the next sticker.
        /// </summary>
        private static void PrintLabelsTspl(string printer, int copies)
        {
            RawPrinterHelper.SendRaw(printer, BuildTsplJob(copies), "Labels");
        }

        /// <summary>The TSPL job for the current print queue (public for the smoke test).</summary>
        public static byte[] BuildTsplJob(int copies)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var job = new List<byte>();
            void Cmd(string line) => job.AddRange(System.Text.Encoding.ASCII.GetBytes(line + "\r\n"));

            // Each label advances exactly sticker height + gap. Relying on the gap sensor made labels
            // creep by one gap per sticker on the XP-365B, so the feed is measured, not sensed.
            Cmd(string.Format(inv, "SIZE {0:0.##} mm,{1:0.##} mm", _wMm, _hMm + Math.Max(0, _gapMm)));
            Cmd("GAP 0 mm,0 mm");
            Cmd("DIRECTION 0,0");
            Cmd("REFERENCE 0,0");
            Cmd("OFFSET 0 mm");
            Cmd("SET PEEL OFF");
            Cmd("SET CUTTER OFF");
            Cmd("SET TEAR ON");

            // Labels to print, honouring the page range (one label = one page on a roll)
            var labels = new List<LabelPrintItem>();
            for (int i = 0; i < _printQueue.Count; i++)
                if (_allowedPages == null || _allowedPages.Contains(i + 1)) labels.Add(_printQueue[i]);

            int k = 0;
            while (k < labels.Count)
            {
                // Same product printed n times -> one image, PRINT 1,n
                int n = 1;
                while (k + n < labels.Count && SameLabel(labels[k], labels[k + n])) n++;
                using (var bmp = RenderLabelBitmap(labels[k]))
                {
                    int bytesPerRow = (bmp.Width + 7) / 8;
                    byte[] data = PackTsplBitmap(bmp, bytesPerRow);
                    Cmd("CLS");
                    job.AddRange(System.Text.Encoding.ASCII.GetBytes(string.Format(inv, "BITMAP 0,0,{0},{1},0,", bytesPerRow, bmp.Height)));
                    job.AddRange(data);
                    Cmd("");
                    Cmd(string.Format(inv, "PRINT 1,{0}", n * copies));
                }
                k += n;
            }
            return job.ToArray();
        }

        /// <summary>Prepares the queue and sizes the way PrintLabels does, without printing (smoke test).</summary>
        public static void PrepareForTest(List<LabelPrintItem> items, LabelPrintOptions options)
        {
            using (BuildDocument(items, options)) { }
        }

        private static bool SameLabel(LabelPrintItem a, LabelPrintItem b) =>
            a.Name == b.Name && a.SKU == b.SKU && a.Price == b.Price;

        /// <summary>TSPL bitmap rows: bit 0 = print (black), bit 1 = blank.</summary>
        private static byte[] PackTsplBitmap(Bitmap bmp, int bytesPerRow)
        {
            var data = new byte[bytesPerRow * bmp.Height];
            for (int i = 0; i < data.Length; i++) data[i] = 0xFF;
            var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var line = new byte[bd.Stride];
                for (int y = 0; y < bmp.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0 + y * bd.Stride, line, 0, bd.Stride);
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int i = x * 4;
                        int lum = (line[i] * 11 + line[i + 1] * 59 + line[i + 2] * 30) / 100;
                        if (lum < 140) data[y * bytesPerRow + (x >> 3)] &= (byte)~(0x80 >> (x & 7));
                    }
                }
            }
            finally { bmp.UnlockBits(bd); }
            return data;
        }

        /// <summary>
        /// The label as a 203 dpi image the size of the sticker: full name (up to two lines, shrunk to fit),
        /// a scannable Code 128 barcode with whole-dot bars, the code and the price. No border.
        /// </summary>
        public static Bitmap RenderLabelBitmap(LabelPrintItem item)
        {
            const float dpi = 203.2f;
            const float dotsPerMm = dpi / 25.4f;
            int W = Math.Max(80, (int)Math.Round(_wMm * dotsPerMm));
            int H = Math.Max(60, (int)Math.Round(_hMm * dotsPerMm));
            int ml = (int)Math.Round(Math.Max(1, _mLeftMm) * dotsPerMm), mr = (int)Math.Round(Math.Max(1, _mRightMm) * dotsPerMm);
            int mt = (int)Math.Round(Math.Max(1, _mTopMm) * dotsPerMm), mb = (int)Math.Round(Math.Max(1, _mBottomMm) * dotsPerMm);
            int areaW = Math.Max(40, W - ml - mr);

            var bmp = new Bitmap(W, H, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpi, dpi);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Pixel;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.SmoothingMode = SmoothingMode.None;

            string name = item.Name ?? "";
            string code = string.IsNullOrWhiteSpace(item.SKU) ? "" : item.SKU.Trim();
            string price = CurrencyService.Format(item.Price);
            bool rtl = StartsRightToLeft(name);

            // Fonts are sized for a 36 mm tall label; smaller/larger labels scale with it
            float k = Math.Max(0.6f, Math.Min(1.6f, (float)(_hMm / 36.0)));
            using var priceFont = new Font("Segoe UI", 11f * k, FontStyle.Bold);
            using var codeFont = new Font("Consolas", 7f * k);
            int priceH = (int)Math.Ceiling(priceFont.GetHeight(g));
            int codeH = code.Length > 0 ? (int)Math.Ceiling(codeFont.GetHeight(g)) : 0;

            // "Arabic name (English name)": each part on its own line, so brackets never end up
            // in the wrong place when mixed Arabic/English text wraps
            var split = System.Text.RegularExpressions.Regex.Match(name, @"^\s*(.+?)\s*\((.+)\)\s*$");
            string line1 = name, line2 = null;
            if (split.Success && StartsRightToLeft(split.Groups[1].Value) && !StartsRightToLeft(split.Groups[2].Value))
            {
                line1 = split.Groups[1].Value;
                line2 = split.Groups[2].Value;
            }

            var nameFmt = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
            if (rtl) nameFmt.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            var oneLine = new StringFormat(nameFmt) { Trimming = StringTrimming.EllipsisCharacter };
            oneLine.FormatFlags |= StringFormatFlags.NoWrap;
            var latinLine = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

            // Biggest font (9pt down to 6pt) that fits: two parts -> one line each, otherwise two wrapped lines
            Font nameFont = null;
            int nameH = 0;
            for (float pt = 9f * k; pt >= 6f * k - 0.01f; pt -= 0.5f)
            {
                nameFont?.Dispose();
                nameFont = new Font("Segoe UI", pt, FontStyle.Bold);
                float lineH = nameFont.GetHeight(g);
                if (line2 != null)
                {
                    nameH = (int)Math.Ceiling(lineH * 2);
                    if (g.MeasureString(line1, nameFont).Width <= areaW && g.MeasureString(line2, nameFont).Width <= areaW) break;
                }
                else
                {
                    var size = g.MeasureString(name, nameFont, areaW, nameFmt);
                    nameH = (int)Math.Ceiling(Math.Min(size.Height, lineH * 2));
                    if (size.Height <= lineH * 2 + 1) break;
                }
            }
            using (nameFont)
            {
                int y = mt;
                if (line2 != null)
                {
                    int lh = nameH / 2;
                    g.DrawString(line1, nameFont, Brushes.Black, new RectangleF(ml, y, areaW, lh), oneLine);
                    g.DrawString(line2, nameFont, Brushes.Black, new RectangleF(ml, y + lh, areaW, lh), latinLine);
                }
                else
                {
                    g.DrawString(name, nameFont, Brushes.Black, new RectangleF(ml, y, areaW, nameH), nameFmt);
                }
                y += nameH + 4;

                int barH = H - mb - priceH - codeH - y - 4;
                if (code.Length > 0 && barH >= 24)
                {
                    int modules = BarcodeService.Code128ModuleCount(code);
                    // Whole-dot module width (2-3 dots scans best); quiet zone 10 modules each side
                    int module = Math.Max(1, Math.Min(3, areaW / (modules + 20)));
                    int bx = (W - modules * module) / 2;
                    BarcodeService.DrawCode128(g, code, bx, y, module, barH);
                    y += barH + 2;
                    var center = new StringFormat { Alignment = StringAlignment.Center };
                    g.DrawString(code, codeFont, Brushes.Black, new RectangleF(ml, y, areaW, codeH), center);
                    y += codeH;
                }
                else
                {
                    y = Math.Max(y, (H - priceH) / 2);
                }
                g.DrawString(price, priceFont, Brushes.Black, new RectangleF(ml, y, areaW, priceH),
                    new StringFormat { Alignment = StringAlignment.Center });
            }
            return bmp;
        }

        private static bool StartsRightToLeft(string s)
        {
            foreach (char c in s ?? "")
            {
                if (c >= 0x0590 && c <= 0x08FF) return true; // Hebrew / Arabic
                if (char.IsLetter(c)) return false;
            }
            return false;
        }

        private static PrintDocument BuildDocument(List<LabelPrintItem> items, LabelPrintOptions options)
        {
            _printQueue = ExpandQueue(items);
            _queueIndex = 0;
            _pageNumber = 0;
            _allowedPages = ParsePageRange(options.PageRange);
            _grayscale = !options.Color;

            var defaults = PrintSettings.GetSnapshot();
            var resolved = PrintSettings.ResolveForPrinter(options.PrinterName, "label", defaults);

            double wMm = options.LabelWidthMm > 0 ? options.LabelWidthMm : resolved.WidthMm;
            double hMm = options.LabelHeightMm > 0 ? options.LabelHeightMm : resolved.HeightMm;
            double gapMm = options.LabelGapMm >= 0 ? options.LabelGapMm : (resolved.GapMm >= 0 ? resolved.GapMm : defaults.LabelGapMm);
            double uniMargin = options.MarginMm >= 0 ? options.MarginMm
                : (resolved.MarginMm >= 0 ? resolved.MarginMm : defaults.LabelMarginMm);
            double mTop = options.MarginTopMm >= 0 ? options.MarginTopMm
                : (resolved.MarginTopMm >= 0 ? resolved.MarginTopMm : uniMargin);
            double mRight = options.MarginRightMm >= 0 ? options.MarginRightMm
                : (resolved.MarginRightMm >= 0 ? resolved.MarginRightMm : uniMargin);
            double mBottom = options.MarginBottomMm >= 0 ? options.MarginBottomMm
                : (resolved.MarginBottomMm >= 0 ? resolved.MarginBottomMm : uniMargin);
            double mLeft = options.MarginLeftMm >= 0 ? options.MarginLeftMm
                : (resolved.MarginLeftMm >= 0 ? resolved.MarginLeftMm : uniMargin);
            int columns = options.Columns >= 0 ? options.Columns
                : (resolved.Columns > 0 ? resolved.Columns : defaults.LabelColumns);
            string mode = !string.IsNullOrWhiteSpace(options.PaperMode)
                ? options.PaperMode
                : (!string.IsNullOrWhiteSpace(resolved.PaperMode) ? resolved.PaperMode : defaults.LabelPaperMode);
            double pageWMm = options.PageWidthMm > 0 ? options.PageWidthMm
                : (resolved.PageWidthMm > 0 ? resolved.PageWidthMm : defaults.LabelPageWidthMm);
            double pageHMm = options.PageHeightMm > 0 ? options.PageHeightMm
                : (resolved.PageHeightMm > 0 ? resolved.PageHeightMm : defaults.LabelPageHeightMm);

            _rollMode = string.Equals(mode, "roll", StringComparison.OrdinalIgnoreCase);
            _wMm = wMm; _hMm = hMm; _gapMm = gapMm;
            _mTopMm = mTop; _mRightMm = mRight; _mBottomMm = mBottom; _mLeftMm = mLeft;

            _labelWidth = Math.Max(1, PrintSettings.MmToHundredths(wMm));
            _labelHeight = Math.Max(1, PrintSettings.MmToHundredths(hMm));
            _gapX = PrintSettings.MmToHundredths(gapMm);
            _gapY = _gapX;
            _maxCols = _rollMode ? 1 : (columns > 0 ? Math.Min(4, columns) : 4);
            int mt = PrintSettings.MmToHundredths(mTop);
            int mr = PrintSettings.MmToHundredths(mRight);
            int mb = PrintSettings.MmToHundredths(mBottom);
            int ml = PrintSettings.MmToHundredths(mLeft);

            PrintDocument pd = new PrintDocument();
            if (_rollMode)
            {
                int pw = Math.Max(_labelWidth + ml + mr, PrintSettings.MmToHundredths(wMm));
                int ph = Math.Max(_labelHeight + mt + mb, PrintSettings.MmToHundredths(hMm));
                try { pd.DefaultPageSettings.PaperSize = new PaperSize("Label", pw, ph); }
                catch { pd.DefaultPageSettings.PaperSize = new PaperSize("Label", pw, ph); }
                pd.DefaultPageSettings.Margins = new Margins(ml, mr, mt, mb);
            }
            else
            {
                int pw = Math.Max(1, PrintSettings.MmToHundredths(pageWMm));
                int ph = Math.Max(1, PrintSettings.MmToHundredths(pageHMm));
                try { pd.DefaultPageSettings.PaperSize = new PaperSize("LabelSheet", pw, ph); }
                catch { pd.DefaultPageSettings.PaperSize = new PaperSize("LabelSheet", pw, ph); }
                pd.DefaultPageSettings.Margins = new Margins(ml, mr, mt, mb);
            }
            pd.DefaultPageSettings.Landscape = options.Landscape;
            pd.DefaultPageSettings.Color = options.Color;

            if (!string.IsNullOrWhiteSpace(options.PrinterName))
            {
                try { pd.PrinterSettings.PrinterName = options.PrinterName; } catch { }
            }

            int copies = Math.Max(1, Math.Min(99, options.Copies));
            try { pd.PrinterSettings.Copies = (short)copies; } catch { }

            try
            {
                PrintSettings.SavePrinterProfile(options.PrinterName, "label", new PrinterJobProfile
                {
                    WidthMm = wMm,
                    HeightMm = hMm,
                    GapMm = gapMm,
                    MarginMm = uniMargin,
                    MarginTopMm = mTop,
                    MarginRightMm = mRight,
                    MarginBottomMm = mBottom,
                    MarginLeftMm = mLeft,
                    Columns = columns,
                    PaperMode = mode,
                    PageWidthMm = pageWMm,
                    PageHeightMm = pageHMm
                });
            }
            catch { }

            pd.PrintPage += Pd_PrintPage;
            return pd;
        }

        private static HashSet<int> ParsePageRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range) ||
                range.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
                return null;

            var set = new HashSet<int>();
            foreach (var part in range.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = part.Trim();
                if (token.Contains('-'))
                {
                    var ends = token.Split('-');
                    if (ends.Length == 2 &&
                        int.TryParse(ends[0].Trim(), out int a) &&
                        int.TryParse(ends[1].Trim(), out int b))
                    {
                        int lo = Math.Min(a, b);
                        int hi = Math.Max(a, b);
                        for (int i = lo; i <= hi; i++)
                            if (i >= 1) set.Add(i);
                    }
                }
                else if (int.TryParse(token, out int page) && page >= 1)
                {
                    set.Add(page);
                }
            }
            return set.Count > 0 ? set : null;
        }

        private static List<LabelPrintItem> ExpandQueue(List<LabelPrintItem> items)
        {
            var queue = new List<LabelPrintItem>();
            foreach (var item in items)
            {
                int copies = Math.Max(1, item.Quantity);
                for (int i = 0; i < copies; i++)
                {
                    queue.Add(new LabelPrintItem
                    {
                        Name = item.Name,
                        SKU = item.SKU,
                        Price = item.Price,
                        Quantity = 1
                    });
                }
            }
            return queue;
        }

        private static void Pd_PrintPage(object sender, PrintPageEventArgs e)
        {
            if (_printQueue == null || _queueIndex >= _printQueue.Count)
            {
                e.HasMorePages = false;
                return;
            }

            _pageNumber++;
            bool include = _allowedPages == null || _allowedPages.Contains(_pageNumber);

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            Rectangle area = e.MarginBounds;
            int lw = Math.Max(40, _labelWidth);
            int lh = Math.Max(30, _labelHeight);
            int gx = Math.Max(0, _gapX);
            int gy = Math.Max(0, _gapY);
            int maxCols = Math.Max(1, _maxCols);
            int cols = _rollMode
                ? 1
                : Math.Max(1, Math.Min(maxCols, (area.Width + gx) / (lw + gx)));
            int rows = _rollMode
                ? 1
                : Math.Max(1, (area.Height + gy) / (lh + gy));
            int perPage = cols * rows;

            int drawn = 0;
            while (_queueIndex < _printQueue.Count && drawn < perPage)
            {
                if (include)
                {
                    int col = drawn % cols;
                    int row = drawn / cols;
                    int x = area.Left + col * (lw + gx);
                    int y = area.Top + row * (lh + gy);
                    DrawLabel(g, _printQueue[_queueIndex], new Rectangle(x, y, lw, lh));
                }
                _queueIndex++;
                drawn++;
            }

            // If this page was skipped by range, keep going until an included page or end
            if (!include && _queueIndex < _printQueue.Count)
            {
                e.HasMorePages = true;
                return;
            }

            if (_allowedPages != null && _queueIndex < _printQueue.Count)
            {
                // More labels remain — only continue if any later page is allowed
                int nextPage = _pageNumber + 1;
                int remaining = _printQueue.Count - _queueIndex;
                int maxPage = nextPage + Math.Max(0, (remaining + perPage - 1) / perPage);
                bool anyLeft = false;
                for (int p = nextPage; p <= maxPage; p++)
                {
                    if (_allowedPages.Contains(p)) { anyLeft = true; break; }
                }
                e.HasMorePages = anyLeft;
                return;
            }

            e.HasMorePages = _queueIndex < _printQueue.Count;
        }

        private static void DrawLabel(Graphics g, LabelPrintItem item, Rectangle bounds)
        {
            Color borderColor = _grayscale ? Color.FromArgb(100, 100, 100) : Color.FromArgb(148, 163, 184);
            Color textColor = Color.Black;
            Color accent = _grayscale ? Color.FromArgb(40, 40, 40) : Color.FromArgb(5, 150, 105);

            using (var border = new Pen(borderColor, Math.Max(1f, bounds.Height / 80f)))
                g.DrawRectangle(border, bounds);

            var center = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            };

            string name = item.Name ?? "";
            int maxName = Math.Max(12, Math.Min(40, bounds.Width / 7));
            if (name.Length > maxName) name = name.Substring(0, Math.Max(1, maxName - 3)) + "...";
            string code = string.IsNullOrWhiteSpace(item.SKU) ? "—" : item.SKU;
            string priceStr = CurrencyService.Format(item.Price);

            float pad = Math.Max(2f, bounds.Width * 0.03f);
            float nameH = Math.Max(10f, bounds.Height * 0.16f);
            float barcodeH = Math.Max(18f, bounds.Height * 0.38f);
            float codeH = Math.Max(9f, bounds.Height * 0.13f);
            float priceH = Math.Max(11f, bounds.Height * 0.16f);
            float namePt = Math.Max(5f, Math.Min(12f, bounds.Height * 0.07f));
            float skuPt = Math.Max(4.5f, Math.Min(10f, bounds.Height * 0.055f));
            float pricePt = Math.Max(5.5f, Math.Min(13f, bounds.Height * 0.075f));

            using (var nameFont = new Font("Segoe UI", namePt, FontStyle.Bold))
            using (var skuFont = new Font("Consolas", skuPt))
            using (var priceFont = new Font("Segoe UI", pricePt, FontStyle.Bold))
            using (var textBrush = new SolidBrush(textColor))
            using (var accentBrush = new SolidBrush(accent))
            {
                float y = bounds.Y + pad;
                var nameRect = new RectangleF(bounds.X + pad, y, bounds.Width - pad * 2, nameH);
                g.DrawString(name, nameFont, textBrush, nameRect, center);
                y += nameH + pad * 0.4f;

                try
                {
                    var bs = new BarcodeService();
                    int targetW = Math.Max(40, (int)(bounds.Width - pad * 2));
                    int targetH = Math.Max(16, (int)barcodeH);
                    using (Bitmap barcode = bs.RenderCode128(code == "—" ? "0" : code, targetW, targetH))
                    {
                        int bx = bounds.X + (bounds.Width - targetW) / 2;
                        var oldMode = g.InterpolationMode;
                        var oldOffset = g.PixelOffsetMode;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor; // keep bars sharp
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(barcode, new Rectangle(bx, (int)y, targetW, targetH));
                        g.InterpolationMode = oldMode;
                        g.PixelOffsetMode = oldOffset;
                    }
                }
                catch
                {
                    // If barcode render fails, still show code text
                }
                y += barcodeH + pad * 0.3f;

                var codeRect = new RectangleF(bounds.X + pad, y, bounds.Width - pad * 2, codeH);
                g.DrawString(code, skuFont, textBrush, codeRect, center);
                y += codeH;

                var priceRect = new RectangleF(bounds.X + pad, y, bounds.Width - pad * 2, priceH);
                g.DrawString(priceStr, priceFont, accentBrush, priceRect, center);
            }
        }
    }
}

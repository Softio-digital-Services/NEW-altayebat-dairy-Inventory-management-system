using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Management;
using System.Runtime.InteropServices;

namespace InventorySystem.Helpers
{
    /// <summary>
    /// Sends bytes straight to a printer (no driver rendering) and builds ESC/POS receipt images.
    /// Needed for printers such as the Xprinter XP-365B: its Windows driver is a TSC label driver that
    /// turns every page into TSPL commands, which the printer prints as text while in receipt mode.
    /// </summary>
    public static class RawPrinterHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private class DOC_INFO_1
        {
            public string pDocName;
            public string pOutputFile;
            public string pDataType;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 di);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool WritePrinter(IntPtr hPrinter, byte[] pBytes, int dwCount, out int dwWritten);

        private static readonly ConcurrentDictionary<string, bool> _tsplDriverCache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Writes the bytes to the printer as one RAW job.</summary>
        public static void SendRaw(string printerName, byte[] data, string docName)
        {
            if (!OpenPrinter(printerName, out IntPtr h, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot open printer \"" + printerName + "\"");
            try
            {
                var di = new DOC_INFO_1 { pDocName = docName, pDataType = "RAW" };
                if (StartDocPrinter(h, 1, di) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start print job");
                try
                {
                    if (!StartPagePrinter(h))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot start page");
                    if (!WritePrinter(h, data, data.Length, out int written) || written != data.Length)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot send data to printer");
                    EndPagePrinter(h);
                }
                finally { EndDocPrinter(h); }
            }
            finally { ClosePrinter(h); }
        }

        /// <summary>
        /// True when Windows prints to this printer through a TSC/TSPL label driver (Seagull), as the
        /// Xprinter label/receipt models install. Receipts must bypass such a driver.
        /// </summary>
        public static bool UsesTsplLabelDriver(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return false;
            return _tsplDriverCache.GetOrAdd(printerName, name =>
            {
                try
                {
                    string driver = null;
                    using (var q = new ManagementObjectSearcher(
                        "SELECT DriverName FROM Win32_Printer WHERE Name='" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'"))
                    {
                        foreach (ManagementObject p in q.Get())
                            driver = p["DriverName"] as string;
                    }
                    if (string.IsNullOrEmpty(driver)) return false;

                    using (var q = new ManagementObjectSearcher("SELECT Name, ConfigFile, DataFile FROM Win32_PrinterDriver"))
                    {
                        foreach (ManagementObject d in q.Get())
                        {
                            string dn = d["Name"] as string ?? "";
                            if (!dn.StartsWith(driver + ",", StringComparison.OrdinalIgnoreCase)) continue;
                            string config = (d["ConfigFile"] as string ?? "").ToLowerInvariant();
                            string data = System.IO.Path.GetFileName(d["DataFile"] as string ?? "").ToLowerInvariant();
                            if (config.Contains("seagull") && (data.StartsWith("tsc") || driver.ToLowerInvariant().Contains("xprinter")))
                                return true;
                        }
                    }
                }
                catch { }
                return false;
            });
        }

        /// <summary>Printable dots across the head (203 dpi = 8 dots/mm) for a receipt roll width.</summary>
        public static int PrintableDots(double paperWidthMm)
        {
            if (paperWidthMm >= 76) return 576;  // 80 mm roll -> 72 mm printable
            if (paperWidthMm >= 56) return 384;  // 58 mm roll -> 48 mm printable
            return Math.Max(64, (int)((paperWidthMm - 8) * 8) / 8 * 8);
        }

        /// <summary>
        /// ESC/POS job: initialise, raster image (GS v 0) in bands, feed and cut. Black = pixels darker than mid-grey.
        /// </summary>
        public static byte[] BuildEscPosRaster(Bitmap image, int copies, int feedLines = 4)
        {
            int w = image.Width, h = image.Height;
            int bytesPerRow = (w + 7) / 8;
            var rows = new byte[bytesPerRow * h];

            var rect = new Rectangle(0, 0, w, h);
            BitmapData bd = image.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var line = new byte[bd.Stride];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(bd.Scan0 + y * bd.Stride, line, 0, bd.Stride);
                    for (int x = 0; x < w; x++)
                    {
                        int i = x * 4;
                        int a = line[i + 3];
                        // Transparent = paper; luminance of B,G,R
                        int lum = a == 0 ? 255 : (line[i] * 11 + line[i + 1] * 59 + line[i + 2] * 30) / 100;
                        if (lum < 140) rows[y * bytesPerRow + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }
            finally { image.UnlockBits(bd); }

            var job = new List<byte>(rows.Length + 64);
            for (int c = 0; c < Math.Max(1, copies); c++)
            {
                job.AddRange(new byte[] { 0x1B, 0x40 });          // ESC @  initialise
                const int band = 255;
                for (int y0 = 0; y0 < h; y0 += band)
                {
                    int bh = Math.Min(band, h - y0);
                    job.AddRange(new byte[] {
                        0x1D, 0x76, 0x30, 0x00,                     // GS v 0, normal size
                        (byte)(bytesPerRow & 0xFF), (byte)(bytesPerRow >> 8),
                        (byte)(bh & 0xFF), (byte)(bh >> 8) });
                    var chunk = new byte[bytesPerRow * bh];
                    Array.Copy(rows, y0 * bytesPerRow, chunk, 0, chunk.Length);
                    job.AddRange(chunk);
                }
                job.AddRange(new byte[] { 0x1B, 0x64, (byte)feedLines }); // ESC d n  feed to tear bar
                job.AddRange(new byte[] { 0x1D, 0x56, 0x42, 0x00 });      // GS V B 0  cut (ignored without cutter)
            }
            return job.ToArray();
        }
    }
}

using System;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Forms;
using QRCoder;
using InventorySystem.Helpers;

namespace InventorySystem.Forms
{
    /// <summary>
    /// Displays a QR code the tablet user can scan to open the Web POS instantly.
    /// Auto-detects the PC's current LAN IP -- no manual configuration needed.
    /// </summary>
    public class ScanToConnectForm : BaseModalForm
    {
        private PictureBox picBox;
        private Label lblUrl;
        private Label lblHint;

        public ScanToConnectForm()
        {
            InitializeComponent();
            LocalizationManager.LanguageChanged += (s, e) => ApplyLocalization();
            ApplyLocalization();
            LoadData();
        }

        private void ApplyLocalization()
        {
            LocalizationManager.ApplyRTL(this);
            this.TitleText = LocalizationManager.GetString("Plugins_ScanTitle");
            if (lblHint != null) lblHint.Text = LocalizationManager.GetString("Dash_ScanHint");
            SetFooterButtons(
                null,
                LocalizationManager.GetString("Popup_Cancel"),
                null,
                (s, e) => this.Close()
            );
        }

        private void InitializeComponent()
        {
            this.Size = new Size(500, 680);
            this.EnforceMinWidth = false;

            TableLayoutPanel tlp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(20)
            };
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 380F)); // QR
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));  // URL
            tlp.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Hint

            // QR Code image
            picBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,

                Size = new Size(340, 340),
                Anchor = AnchorStyles.None,
                Margin = new Padding(10)
            };
            // Border for QR code
            picBox.Paint += (s, e) =>
            {
                using var pen = new Pen(ThemeConfig.PrimaryColor, 2);
                e.Graphics.DrawRectangle(pen, 0, 0, picBox.Width - 1, picBox.Height - 1);
            };

            // URL label
            lblUrl = new Label
            {
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = ThemeConfig.PrimaryColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            lblUrl.Click += (s, e) =>
            {
                string url = GetServerUrl();
                Clipboard.SetText(url);
                string original = lblUrl.Text;
                lblUrl.Text = LocalizationManager.GetString("Msg_Copied");
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (_, __) => { lblUrl.Text = original; t.Stop(); };
                t.Start();
            };

            // Hint label
            lblHint = new Label
            {
                Font = ThemeConfig.StandardFont,
                ForeColor = ThemeConfig.SecondaryColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 10, 0, 0)
            };

            tlp.Controls.Add(picBox, 0, 0);
            tlp.Controls.Add(lblUrl, 0, 1);
            tlp.Controls.Add(lblHint, 0, 2);

            this.ContentPanel.Controls.Add(tlp);
        }

        private void LoadData()
        {
            string url = GetServerUrl();
            lblUrl.Text = url;
            picBox.Image = GenerateQrBitmap(url, 300);
        }

        public static string GetServerUrl()
        {
            string ip = GetLocalIpAddress();
            return $"http://{ip}:{AppHostConfig.HttpPort}";
        }

        /// <summary>
        /// The address phones should use. Prefers the real Wi-Fi/Ethernet connection that has a router
        /// (gateway); VPN, Hyper-V/WSL and VirtualBox adapters often come first in Windows' list and
        /// would put an unreachable address in the QR code.
        /// </summary>
        public static string GetLocalIpAddress()
        {
            try
            {
                string best = null;
                int bestScore = int.MinValue;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var props = ni.GetIPProperties();
                    bool hasGateway = props.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    string desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    bool looksVirtual = desc.Contains("virtual") || desc.Contains("vethernet") || desc.Contains("hyper-v")
                        || desc.Contains("vmware") || desc.Contains("virtualbox") || desc.Contains("wsl")
                        || desc.Contains("vpn") || desc.Contains("tap-") || desc.Contains("tunnel") || desc.Contains("bluetooth");

                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(addr.Address)) continue;
                        string ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254")) continue;

                        int score = 0;
                        if (hasGateway) score += 100;
                        if (!looksVirtual) score += 50;
                        if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                            || ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score += 20;
                        if (score > bestScore) { bestScore = score; best = ip; }
                    }
                }
                if (best != null) return best;
            }
            catch { }
            return "localhost";
        }

        private static Bitmap GenerateQrBitmap(string url, int pixelsPerModule)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.M);
            using var qrCode = new QRCode(qrData);
            return qrCode.GetGraphic(pixelsPerModule / 21);
        }
    }
}

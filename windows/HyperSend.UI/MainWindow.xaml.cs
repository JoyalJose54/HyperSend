using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HyperSend.Core;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace HyperSend.UI
{
    // ─────────────────────────────────────────────
    // Simple file item model for the ListView
    // ─────────────────────────────────────────────
    public class TransferItem
    {
        public string Icon { get; set; } = "📄";
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long FileSize { get; set; }
        public string DisplaySize { get; set; } = string.Empty;
        public string Status { get; set; } = "Ready";
    }

    public partial class MainWindow : Window
    {
        // ─────────────────────────────────────────────
        // State machine
        // ─────────────────────────────────────────────
        private enum AppState { Idle, PairingReceiver, PairingSender, Transferring, Complete }
        private AppState _state = AppState.Idle;
        private bool _isSender;

        // ─────────────────────────────────────────────
        // Core fields
        // ─────────────────────────────────────────────
        private static readonly byte[] _defaultSalt = new byte[16] {
            0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A,
            0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A
        };

        private readonly ObservableCollection<TransferItem> _items = new();
        private CancellationTokenSource? _transferCts;
        private string _saveDirectory;

        // Transfer result captured for CompletePanel
        private double _finalAvgMBps;
        private long _finalTotalBytes;
        private double _finalElapsedSec;

        // Arc geometry constants
        private const double ArcRadius = 110.0;
        private const double ArcCenterX = 140.0;
        private const double ArcCenterY = 140.0;

        // Progress bar container width (synced to border width in XAML)
        private double _progressBarContainerWidth = 520.0;

        // Storyboards (looked up after InitializeComponent)
        private Storyboard? _pulseStoryboard;

        // ─────────────────────────────────────────────
        // Constructor
        // ─────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();

            FileList.ItemsSource = _items;

            string downloads = IOPath.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _saveDirectory = IOPath.Combine(downloads, "HyperSend");

            // Set hostname badge
            TxtHostname.Text = System.Net.Dns.GetHostName();
            TxtDevicePcName.Text = System.Net.Dns.GetHostName();

            // Start link health refresh
            RefreshLinkHealth();
            var linkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            linkTimer.Tick += (_, _) => RefreshLinkHealth();
            linkTimer.Start();

            // Start idle pulse animation
            Loaded += (_, _) =>
            {
                _pulseStoryboard = (Storyboard)Resources["PulseAnimation"];
                _pulseStoryboard?.Begin();

                // Draw the background arc (static full circle)
                DrawBackgroundArc();
                // Start at 0
                UpdateProgressArc(0);
            };
        }

        // ─────────────────────────────────────────────
        // Network / Link Health
        // ─────────────────────────────────────────────
        private void RefreshLinkHealth()
        {
            Task.Run(() =>
            {
                try
                {
                    var health = NetworkDiscovery.QueryLinkHealth();
                    var gw = NetworkDiscovery.GetPhoneHotspotGatewayIp();
                    string localIp = GetLocalIp();

                    Dispatcher.Invoke(() =>
                    {
                        if (health.IsOptimal5GHzAx)
                        {
                            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
                            TxtLinkStatus.Text = $"5 GHz Wi-Fi 6  ·  {health.Band}  ·  {health.TransmitRateMbps:F0} Mbps";
                        }
                        else if (!string.IsNullOrWhiteSpace(health.Ssid) && health.Ssid != "Unknown")
                        {
                            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
                            TxtLinkStatus.Text = $"{health.Band}  {health.RadioType}  (not Wi-Fi 6)";
                        }
                        else
                        {
                            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
                            TxtLinkStatus.Text = "No Wi-Fi link detected";
                        }

                        TxtLocalIp.Text = localIp;

                        // Auto-fill phone IP if TextBox is empty
                        if (string.IsNullOrWhiteSpace(TxtPhoneIp.Text) && gw != null)
                            TxtPhoneIp.Text = gw.ToString();
                    });
                }
                catch { }
            });
        }

        private static string GetLocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return ua.Address.ToString();
                    }
                }
            }
            catch { }
            return "";
        }

        // ─────────────────────────────────────────────
        // Panel / State transitions
        // ─────────────────────────────────────────────
        private void ShowPanel(AppState newState)
        {
            _state = newState;

            // Hide all panels first
            IdlePanel.Visibility = Visibility.Collapsed;
            PairingPanel.Visibility = Visibility.Collapsed;
            TransferPanel.Visibility = Visibility.Collapsed;
            CompletePanel.Visibility = Visibility.Collapsed;

            switch (newState)
            {
                case AppState.Idle:
                    IdlePanel.Visibility = Visibility.Visible;
                    _pulseStoryboard?.Begin();
                    break;

                case AppState.PairingReceiver:
                    PairingPanel.Visibility = Visibility.Visible;
                    TxtPairingTitle.Text = "Waiting for Connection";
                    ReceiverSubPanel.Visibility = Visibility.Visible;
                    SenderSubPanel.Visibility = Visibility.Collapsed;
                    string pin = GeneratePin();
                    TxtPinDisplay.Text = pin;
                    break;

                case AppState.PairingSender:
                    PairingPanel.Visibility = Visibility.Visible;
                    TxtPairingTitle.Text = "Connect to Phone";
                    ReceiverSubPanel.Visibility = Visibility.Collapsed;
                    SenderSubPanel.Visibility = Visibility.Visible;
                    // Auto-detect phone IP
                    if (string.IsNullOrWhiteSpace(TxtPhoneIp.Text))
                    {
                        var gw = NetworkDiscovery.GetPhoneHotspotGatewayIp();
                        if (gw != null) TxtPhoneIp.Text = gw.ToString();
                    }
                    break;

                case AppState.Transferring:
                    TransferPanel.Visibility = Visibility.Visible;
                    // Set roles
                    if (_isSender)
                    {
                        TxtPcRole.Text = "SENDING";
                        TxtPcRole.Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
                        PcRoleBadge.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x25, 0x63, 0xEB));
                        TxtPhoneRole.Text = "RECEIVING";
                        TxtPhoneRole.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0xC6, 0xDA));
                        PhoneRoleBadge.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x26, 0xC6, 0xDA));
                        TxtTargetLabel.Text = "🎯 Target: ≥65 MB/s (PC→Phone)";
                    }
                    else
                    {
                        TxtPcRole.Text = "RECEIVING";
                        TxtPcRole.Foreground = new SolidColorBrush(Color.FromRgb(0x26, 0xC6, 0xDA));
                        PcRoleBadge.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x26, 0xC6, 0xDA));
                        TxtPhoneRole.Text = "SENDING";
                        TxtPhoneRole.Foreground = new SolidColorBrush(Color.FromRgb(0x4F, 0xC3, 0xF7));
                        PhoneRoleBadge.Background = new SolidColorBrush(Color.FromArgb(0x1A, 0x25, 0x63, 0xEB));
                        TxtTargetLabel.Text = "🎯 Target: ≥90 MB/s (Phone→PC)";
                    }
                    TxtTargetStatus.Text = "";
                    // Start flow dot animations
                    StartFlowAnimations();
                    // Reset arc
                    UpdateProgressArc(0);
                    break;

                case AppState.Complete:
                    CompletePanel.Visibility = Visibility.Visible;
                    // Fill summary
                    TxtCompleteSpeed.Text = $"{_finalAvgMBps:F1} MB/s";
                    TxtCompleteSize.Text = FormatBytes(_finalTotalBytes);
                    TxtCompleteTime.Text = $"{_finalElapsedSec:F1}s";
                    double targetThreshold = _isSender ? 65.0 : 90.0;
                    TrophyBadge.Visibility = _finalAvgMBps >= targetThreshold
                        ? Visibility.Visible : Visibility.Collapsed;
                    break;
            }
        }

        // ─────────────────────────────────────────────
        // Idle panel buttons
        // ─────────────────────────────────────────────
        private void BtnSendToPhone_Click(object sender, RoutedEventArgs e)
        {
            _isSender = true;
            ShowPanel(AppState.PairingSender);
        }

        private void BtnReceiveFromPhone_Click(object sender, RoutedEventArgs e)
        {
            _isSender = false;
            TxtPinDisplay.Text = GeneratePin();
            ShowPanel(AppState.PairingReceiver);
            // Start listening in background
            StartReceiverPairing();
        }

        // ─────────────────────────────────────────────
        // Pairing panel
        // ─────────────────────────────────────────────
        private void BtnPairingBack_Click(object sender, RoutedEventArgs e)
        {
            _transferCts?.Cancel();
            ShowPanel(AppState.Idle);
        }

        private string GeneratePin()
        {
            var rng = new Random();
            return rng.Next(100000, 999999).ToString();
        }

        // ─────────────────────────────────────────────
        // Sender connect & send
        // ─────────────────────────────────────────────
        private async void BtnConnectAndSend_Click(object sender, RoutedEventArgs e)
        {
            string ip = TxtPhoneIp.Text.Trim();
            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("Please enter the phone IP address.", "IP Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pin = TxtPin.Text.Trim();
            if (pin.Length != 6 || !pin.All(char.IsDigit))
            {
                MessageBox.Show("Please enter the 6-digit PIN shown on the phone.", "PIN Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_items.Count == 0)
            {
                _items.Add(new TransferItem
                {
                    Name = "High-Speed Benchmark Stream (2 GB)",
                    FileSize = 2L * 1024 * 1024 * 1024,
                    DisplaySize = "2.0 GB",
                    FullPath = "",
                    IsDirectory = false,
                    Icon = "⚡",
                    Status = "Ready"
                });
            }

            _transferCts = new CancellationTokenSource();
            ShowPanel(AppState.Transferring);

            try
            {
                byte[] sessionKey = CryptoEngine.DerivePinKey(pin, _defaultSalt);
                ThroughputMeter? lastMeter = null;

                var progress = new Progress<TransferProgress>(p => Dispatcher.Invoke(() => UpdateTransferUI(p)));

                foreach (var item in _items)
                {
                    TxtCurrentFile.Text = item.Name;

                    if (item.IsDirectory)
                    {
                        var (largeFiles, bundles) = BundlePacker.ScanDirectory(item.FullPath);
                        foreach (var bundle in bundles)
                        {
                            byte[] packed = await BundlePacker.PackBundleAsync(bundle, _transferCts.Token);
                            string tempPath = IOPath.GetTempFileName();
                            try
                            {
                                await File.WriteAllBytesAsync(tempPath, packed, _transferCts.Token);
                                var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort,
                                    sessionKey, 1L, 6, NetworkConfig.DefaultChunkSize);
                                lastMeter = await client.RunSenderAsync(tempPath, packed.Length,
                                    progress, _transferCts.Token);
                            }
                            finally { if (File.Exists(tempPath)) File.Delete(tempPath); }
                        }
                        foreach (var large in largeFiles)
                        {
                            var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort,
                                sessionKey, 1L, 6, NetworkConfig.DefaultChunkSize);
                            lastMeter = await client.RunSenderAsync(large.AbsoluteSourcePath,
                                large.FileSize, progress, _transferCts.Token);
                        }
                    }
                    else
                    {
                        var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort,
                            sessionKey, 1L, 6, NetworkConfig.DefaultChunkSize);
                        string? filePath = string.IsNullOrEmpty(item.FullPath) ? null : item.FullPath;
                        lastMeter = await client.RunSenderAsync(filePath, item.FileSize,
                            progress, _transferCts.Token);
                    }
                }

                if (lastMeter != null)
                {
                    var (avg, _, total, elapsed) = lastMeter.GetSummary();
                    _finalAvgMBps = avg;
                    _finalTotalBytes = total;
                    _finalElapsedSec = elapsed;
                }
                ShowPanel(AppState.Complete);
            }
            catch (OperationCanceledException)
            {
                ShowPanel(AppState.Idle);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Transfer failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                ShowPanel(AppState.PairingSender);
            }
            finally
            {
                _transferCts = null;
            }
        }

        // ─────────────────────────────────────────────
        // Receiver pairing + transfer
        // ─────────────────────────────────────────────
        private void StartReceiverPairing()
        {
            // The PIN is already displayed. We pick it up when the sender connects.
            // We'll prompt for PIN confirmation (the user manually checks it matches).
            // Then start listening.
            string pin = TxtPinDisplay.Text;
            if (pin.Length != 6) return;

            _transferCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] sessionKey = CryptoEngine.DerivePinKey(pin, _defaultSalt);

                    Directory.CreateDirectory(_saveDirectory);
                    string dest = IOPath.Combine(_saveDirectory,
                        $"HyperSend_{DateTime.Now:yyyyMMdd_HHmmss}.dat");

                    var receiver = new EncryptedChunkedReceiver(sessionKey, 1L,
                        NetworkConfig.DefaultPort, 6);

                    var progress = new Progress<TransferProgress>(p =>
                        Dispatcher.Invoke(() => UpdateTransferUI(p)));

                    var meter = await receiver.RunReceiverAsync(dest, 0,
                        NetworkConfig.DefaultChunkSize, progress, _transferCts.Token);

                    var (avg, _, total, elapsed) = meter.GetSummary();
                    _finalAvgMBps = avg;
                    _finalTotalBytes = total;
                    _finalElapsedSec = elapsed;

                    Dispatcher.Invoke(() => ShowPanel(AppState.Complete));
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() => ShowPanel(AppState.Idle));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"Receiver error: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        ShowPanel(AppState.Idle);
                    });
                }
                finally
                {
                    _transferCts = null;
                }
            });
        }

        // ─────────────────────────────────────────────
        // Transfer UI update (called from Progress<T>)
        // ─────────────────────────────────────────────
        private void UpdateTransferUI(TransferProgress p)
        {
            if (_state != AppState.Transferring && p.BytesTransferred > 0)
            {
                ShowPanel(AppState.Transferring);
            }

            double fraction = p.TotalBytes > 0
                ? Math.Clamp((double)p.BytesTransferred / p.TotalBytes, 0.0, 1.0)
                : 0.0;

            // Arc
            UpdateProgressArc(fraction);

            // Center text
            TxtLiveSpeed.Text = $"{p.CurrentMBps:F1}";
            TxtProgressPct.Text = $"{p.Percentage:F1}%";

            // Stats
            TxtAvgSpeed.Text = $"{p.AverageMBps:F1} MB/s";
            TxtTransferred.Text = FormatBytes(p.BytesTransferred);

            if (p.CurrentMBps > 0.5 && p.TotalBytes > 0)
            {
                double remaining = (p.TotalBytes - p.BytesTransferred) / (1024.0 * 1024.0);
                double eta = remaining / p.CurrentMBps;
                TxtEta.Text = eta < 60 ? $"{eta:F0}s" : $"{eta / 60:F1}m";
            }
            else
            {
                TxtEta.Text = "--";
            }

            // Linear progress bar fill
            double containerWidth = ProgressBarFill.ActualWidth > 0
                ? ((System.Windows.FrameworkElement)ProgressBarFill.Parent).ActualWidth
                : _progressBarContainerWidth;
            ProgressBarFill.Width = containerWidth * fraction;

            // Target status
            double threshold = _isSender ? 65.0 : 90.0;
            if (p.AverageMBps >= threshold)
            {
                TxtTargetStatus.Text = $"✅ Exceeding target ({p.AverageMBps:F1} MB/s)";
                TxtTargetStatus.Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
            }
            else
            {
                TxtTargetStatus.Text = $"⚠ {p.AverageMBps:F1} MB/s (below target)";
                TxtTargetStatus.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));
            }
        }

        // ─────────────────────────────────────────────
        // Arc drawing
        // ─────────────────────────────────────────────
        private void DrawBackgroundArc()
        {
            // Draw a full 360° circle as background track
            double r = ArcRadius;
            double cx = ArcCenterX;
            double cy = ArcCenterY;

            // Use a nearly-full arc (359.99°) so it renders as full circle
            double endAngle = -Math.PI / 2 + 2 * Math.PI * 0.9999;
            double x1 = cx + r * Math.Cos(-Math.PI / 2);
            double y1 = cy + r * Math.Sin(-Math.PI / 2);
            double x2 = cx + r * Math.Cos(endAngle);
            double y2 = cy + r * Math.Sin(endAngle);

            var seg = new ArcSegment(new Point(x2, y2), new Size(r, r), 0,
                true, SweepDirection.Clockwise, true);
            var fig = new PathFigure(new Point(x1, y1), new[] { seg }, false);
            ArcBackground.Data = new PathGeometry(new[] { fig });
        }

        private void UpdateProgressArc(double progressFraction)
        {
            progressFraction = Math.Clamp(progressFraction, 0.0, 0.9999);

            double r = ArcRadius;
            double cx = ArcCenterX;
            double cy = ArcCenterY;

            double startAngle = -Math.PI / 2;
            double endAngle = startAngle + 2 * Math.PI * progressFraction;

            double x1 = cx + r * Math.Cos(startAngle);
            double y1 = cy + r * Math.Sin(startAngle);
            double x2 = cx + r * Math.Cos(endAngle);
            double y2 = cy + r * Math.Sin(endAngle);

            if (progressFraction < 0.001)
            {
                // Don't draw tiny arcs — just clear
                ProgressArcPath.Data = null;
                return;
            }

            bool isLargeArc = progressFraction > 0.5;
            var arcSeg = new ArcSegment(new Point(x2, y2), new Size(r, r), 0,
                isLargeArc, SweepDirection.Clockwise, true);
            var pathFig = new PathFigure(new Point(x1, y1), new[] { arcSeg }, false);
            ProgressArcPath.Data = new PathGeometry(new[] { pathFig });
        }

        // ─────────────────────────────────────────────
        // Flow dot animations (TransferPanel)
        // ─────────────────────────────────────────────
        private void StartFlowAnimations()
        {
            foreach (string key in new[] {
                "FlowDot1Anim","FlowDot2Anim","FlowDot3Anim","FlowDot4Anim","FlowDot5Anim"})
            {
                ((Storyboard)Resources[key]).Begin();
            }
        }

        // ─────────────────────────────────────────────
        // Cancel transfer
        // ─────────────────────────────────────────────
        private void BtnCancelTransfer_Click(object sender, RoutedEventArgs e)
        {
            _transferCts?.Cancel();
            // ShowPanel(Idle) will be called in the finally block of the async task
        }

        // ─────────────────────────────────────────────
        // Complete panel — Done button
        // ─────────────────────────────────────────────
        private void BtnDone_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            ShowPanel(AppState.Idle);
        }

        // ─────────────────────────────────────────────
        // Drag & Drop + File management
        // ─────────────────────────────────────────────
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddPaths(files);

                // If we're on idle, switch to sender pairing
                if (_state == AppState.Idle)
                {
                    _isSender = true;
                    ShowPanel(AppState.PairingSender);
                }
            }
        }

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            BtnAddFiles_Click(sender, e);
        }

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Multiselect = true, Title = "Select files to send" };
            if (dlg.ShowDialog() == true) AddPaths(dlg.FileNames);
        }

        private void BtnClearFiles_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
        }

        private void AddPaths(string[] paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    long dirSize = 0;
                    try { dirSize = new DirectoryInfo(p)
                        .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
                    catch { }
                    _items.Add(new TransferItem
                    {
                        Icon = "📁", Name = IOPath.GetFileName(p), FullPath = p,
                        IsDirectory = true, FileSize = dirSize,
                        DisplaySize = FormatBytes(dirSize), Status = "Folder"
                    });
                }
                else if (File.Exists(p))
                {
                    var fi = new FileInfo(p);
                    _items.Add(new TransferItem
                    {
                        Icon = "📄", Name = fi.Name, FullPath = p,
                        IsDirectory = false, FileSize = fi.Length,
                        DisplaySize = FormatBytes(fi.Length), Status = "Ready"
                    });
                }
            }
        }

        // ─────────────────────────────────────────────
        // Utility
        // ─────────────────────────────────────────────
        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}

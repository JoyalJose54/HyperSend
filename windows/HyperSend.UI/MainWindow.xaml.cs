using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HyperSend.Core;
using Microsoft.Win32;
using IOPath = System.IO.Path;

namespace HyperSend.UI
{
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
        private static readonly byte[] DefaultFixedSalt = new byte[16] {
            0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A,
            0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A, 0x5A
        };

        private readonly ObservableCollection<TransferItem> _items = new();
        private readonly List<double> _speedSamples = new();
        private readonly DispatcherTimer _linkRefreshTimer;
        private CancellationTokenSource? _activeTransferCts;
        private bool _isReceiverRunning = false;
        private CancellationTokenSource? _receiverCts;
        private string _saveDirectory;

        public MainWindow()
        {
            InitializeComponent();

            FileList.ItemsSource = _items;

            string downloads = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            _saveDirectory = IOPath.Combine(downloads, "HyperSend");
            TxtSaveDir.Text = $"Save to: {_saveDirectory}";

            RefreshLinkHealth();

            _linkRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _linkRefreshTimer.Tick += (s, e) => RefreshLinkHealth();
            _linkRefreshTimer.Start();
        }

        private void RefreshLinkHealth()
        {
            try
            {
                var health = NetworkDiscovery.QueryLinkHealth();
                TxtLinkSsid.Text = $"SSID: {health.Ssid}";
                TxtBand.Text = health.Band;
                TxtRadio.Text = health.RadioType;
                TxtChannel.Text = health.Channel > 0 ? health.Channel.ToString() : "--";
                TxtRates.Text = $"{health.ReceiveRateMbps} / {health.TransmitRateMbps} Mbps";

                if (health.IsOptimal5GHzAx)
                {
                    HealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#065F46"));
                    HealthBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    HealthBadgeText.Text = "Optimal Wi-Fi 6 (5 GHz)";
                    AdvicePanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    HealthBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78350F"));
                    HealthBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A"));
                    HealthBadgeText.Text = "Suboptimal Link";

                    AdvicePanel.Visibility = Visibility.Visible;
                    TxtAdvice.Text = "Advice: On Phone Hotspot settings, switch Band to 5 GHz and turn ON 'Support Wi-Fi 6' to reach >90 MB/s.";
                }

                // Auto-detect gateway if not already set or default
                if (TxtPhoneIp.Text == "Auto-detecting..." || string.IsNullOrWhiteSpace(TxtPhoneIp.Text))
                {
                    var gw = NetworkDiscovery.GetPhoneHotspotGatewayIp();
                    if (gw != null)
                    {
                        TxtPhoneIp.Text = gw.ToString();
                        TxtStatusLog.Text = $"Auto-detected Phone Gateway: {gw}";
                    }
                }
            }
            catch (Exception ex)
            {
                TxtStatusLog.Text = $"Network scan error: {ex.Message}";
            }
        }

        private void BtnRefreshLink_Click(object sender, RoutedEventArgs e)
        {
            RefreshLinkHealth();
        }

        private void BtnAutoIp_Click(object sender, RoutedEventArgs e)
        {
            var gw = NetworkDiscovery.GetPhoneHotspotGatewayIp();
            if (gw != null)
            {
                TxtPhoneIp.Text = gw.ToString();
                TxtStatusLog.Text = $"Found Phone IP: {gw}";
            }
            else
            {
                TxtStatusLog.Text = "Could not detect active gateway. Please enter phone IP manually.";
            }
        }

        #region Drag and Drop & File Management

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                AddPaths(files);
            }
        }

        private void DropZone_MouseDown(object sender, MouseButtonEventArgs e)
        {
            BtnAddFiles_Click(sender, e);
        }

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to send via HyperSend"
            };

            if (dlg.ShowDialog() == true)
            {
                AddPaths(dlg.FileNames);
            }
        }

        private void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Select folder to send via HyperSend"
            };

            if (dlg.ShowDialog() == true)
            {
                AddPaths(new[] { dlg.FolderName });
            }
        }

        private void AddPaths(string[] paths)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p))
                {
                    long dirSize = 0;
                    try
                    {
                        var di = new DirectoryInfo(p);
                        dirSize = di.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                    }
                    catch { }

                    _items.Add(new TransferItem
                    {
                        Icon = "📁",
                        Name = IOPath.GetFileName(p),
                        FullPath = p,
                        IsDirectory = true,
                        FileSize = dirSize,
                        DisplaySize = FormatBytes(dirSize),
                        Status = "Folder"
                    });
                }
                else if (File.Exists(p))
                {
                    var fi = new FileInfo(p);
                    _items.Add(new TransferItem
                    {
                        Icon = "📄",
                        Name = fi.Name,
                        FullPath = p,
                        IsDirectory = false,
                        FileSize = fi.Length,
                        DisplaySize = FormatBytes(fi.Length),
                        Status = "Ready"
                    });
                }
            }

            UpdateSelectionSummary();
        }

        private void BtnClearFiles_Click(object sender, RoutedEventArgs e)
        {
            _items.Clear();
            UpdateSelectionSummary();
        }

        private void UpdateSelectionSummary()
        {
            long totalBytes = _items.Sum(i => i.FileSize);
            TxtSelectedTotal.Text = $"{_items.Count} items ({FormatBytes(totalBytes)})";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion

        #region Sender Execution

        private async void BtnSend_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTransferCts != null)
            {
                _activeTransferCts.Cancel();
                BtnSend.Content = "⚡ Send to Phone";
                TxtTransferState.Text = "Cancelled";
                return;
            }

            string ip = TxtPhoneIp.Text.Trim();
            if (string.IsNullOrEmpty(ip) || ip == "Auto-detecting...")
            {
                MessageBox.Show("Please enter or auto-detect a valid Phone Hotspot IP address.", "Invalid IP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_items.Count == 0)
            {
                MessageBox.Show("Please drag & drop files or folders to send.", "No Files Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string pin = TxtPin.Text.Trim();
            if (pin.Length != 6)
            {
                MessageBox.Show("Please enter the 6-digit PIN shown on the phone screen for AES-256-GCM authenticated pairing.", "PIN Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _activeTransferCts = new CancellationTokenSource();
            BtnSend.Content = "⛔ Cancel Transfer";
            BtnSend.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtTransferState.Text = "Connecting & Encrypting...";
            _speedSamples.Clear();
            DrawGraph();

            try
            {
                byte[] sessionKey = CryptoEngine.DerivePinKey(pin, DefaultFixedSalt);
                int streamCount = 6;
                int chunkSize = NetworkConfig.DefaultChunkSize;

                var progress = new Progress<TransferProgress>(p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        TransferProgressBar.Value = p.Percentage;
                        TxtLiveSpeed.Text = $"{p.CurrentMBps:F1} MB/s";
                        TxtAverageSpeed.Text = $"Avg: {p.AverageMBps:F1} MB/s";
                        TxtProgressBytes.Text = $"{FormatBytes(p.BytesTransferred)} / {FormatBytes(p.TotalBytes)} ({p.Percentage:F1}%)";
                        TxtElapsedTime.Text = $"{p.ElapsedSeconds:F1}s elapsed";

                        double remainingBytes = p.TotalBytes - p.BytesTransferred;
                        if (p.CurrentMBps > 1.0)
                        {
                            double eta = (remainingBytes / (1024.0 * 1024.0)) / p.CurrentMBps;
                            TxtEta.Text = $"ETA: {eta:F1}s";
                        }

                        _speedSamples.Add(p.CurrentMBps);
                        if (_speedSamples.Count > 60) _speedSamples.RemoveAt(0);
                        DrawGraph();
                    });
                });

                // Execute transfer for each item or directory bundle
                foreach (var item in _items)
                {
                    item.Status = "Sending...";
                    FileList.Items.Refresh();

                    if (item.IsDirectory)
                    {
                        var (largeFiles, bundles) = BundlePacker.ScanDirectory(item.FullPath);
                        TxtStatusLog.Text = $"Packed {bundles.Count} small-file bundles and {largeFiles.Count} large files for {item.Name}";

                        // Send bundled data
                        foreach (var bundle in bundles)
                        {
                            byte[] packed = await BundlePacker.PackBundleAsync(bundle, _activeTransferCts.Token);
                            string tempBundlePath = IOPath.GetTempFileName();
                            try
                            {
                                await File.WriteAllBytesAsync(tempBundlePath, packed, _activeTransferCts.Token);
                                var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort, sessionKey, sessionId: 1L, streamCount: streamCount, chunkSize: chunkSize);
                                await client.RunSenderAsync(tempBundlePath, packed.Length, progress, _activeTransferCts.Token);
                            }
                            finally
                            {
                                if (File.Exists(tempBundlePath)) File.Delete(tempBundlePath);
                            }
                        }

                        // Send large files individually
                        foreach (var large in largeFiles)
                        {
                            var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort, sessionKey, sessionId: 1L, streamCount: streamCount, chunkSize: chunkSize);
                            await client.RunSenderAsync(large.AbsoluteSourcePath, large.FileSize, progress, _activeTransferCts.Token);
                        }
                    }
                    else
                    {
                        var client = new EncryptedChunkedSender(ip, NetworkConfig.DefaultPort, sessionKey, sessionId: 1L, streamCount: streamCount, chunkSize: chunkSize);
                        await client.RunSenderAsync(item.FullPath, item.FileSize, progress, _activeTransferCts.Token);
                    }

                    item.Status = "Completed ✓";
                    FileList.Items.Refresh();
                }

                TxtTransferState.Text = "Transfer Finished Successfully!";
                TxtStatusLog.Text = $"All items transferred. Verified with AES-256-GCM AEAD.";
            }
            catch (OperationCanceledException)
            {
                TxtTransferState.Text = "Transfer Cancelled";
                TxtStatusLog.Text = "Transfer was stopped by user.";
            }
            catch (Exception ex)
            {
                TxtTransferState.Text = "Error";
                TxtStatusLog.Text = $"Transfer error: {ex.Message}";
                MessageBox.Show($"Transfer failed: {ex.Message}", "Transfer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _activeTransferCts = null;
                BtnSend.Content = "⚡ Send to Phone";
                BtnSend.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
            }
        }

        #endregion

        #region Receiver Service Execution

        private void BtnChangeSaveDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Choose destination directory for incoming HyperSend transfers"
            };

            if (dlg.ShowDialog() == true)
            {
                _saveDirectory = dlg.FolderName;
                TxtSaveDir.Text = $"Save to: {_saveDirectory}";
            }
        }

        private void BtnToggleReceiver_Click(object sender, RoutedEventArgs e)
        {
            if (_isReceiverRunning)
            {
                _receiverCts?.Cancel();
                _isReceiverRunning = false;
                BtnToggleReceiver.Content = "Start Receiver";
                BtnToggleReceiver.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                TxtStatusLog.Text = "Receiver stopped.";
                return;
            }

            string pin = TxtPin.Text.Trim();
            if (pin.Length != 6)
            {
                MessageBox.Show("Please enter a 6-digit PIN so incoming connections can be authenticated with AES-256-GCM.", "PIN Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Directory.CreateDirectory(_saveDirectory);
            _receiverCts = new CancellationTokenSource();
            _isReceiverRunning = true;
            BtnToggleReceiver.Content = "Stop Receiver";
            BtnToggleReceiver.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
            TxtStatusLog.Text = $"Receiver listening on 0.0.0.0:{NetworkConfig.DefaultPort}...";

            _ = Task.Run(async () =>
            {
                try
                {
                    byte[] sessionKey = CryptoEngine.DerivePinKey(pin, DefaultFixedSalt);
                    var receiver = new EncryptedChunkedReceiver(sessionKey, expectedSessionId: 1L, port: NetworkConfig.DefaultPort, streamCount: 6);

                    var progress = new Progress<TransferProgress>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            TransferProgressBar.Value = p.Percentage;
                            TxtLiveSpeed.Text = $"{p.CurrentMBps:F1} MB/s";
                            TxtAverageSpeed.Text = $"Avg: {p.AverageMBps:F1} MB/s";
                            TxtProgressBytes.Text = $"{FormatBytes(p.BytesTransferred)} / {FormatBytes(p.TotalBytes)}";
                            TxtElapsedTime.Text = $"{p.ElapsedSeconds:F1}s elapsed";

                            _speedSamples.Add(p.CurrentMBps);
                            if (_speedSamples.Count > 60) _speedSamples.RemoveAt(0);
                            DrawGraph();
                        });
                    });

                    // Target receive file
                    string destPath = IOPath.Combine(_saveDirectory, $"HyperSend_Received_{DateTime.Now:yyyyMMdd_HHmmss}.dat");
                    var meter = await receiver.RunReceiverAsync(destPath, 0, NetworkConfig.DefaultChunkSize, progress, _receiverCts.Token);

                    var (avgMBps, avgMbps, totalBytes, elapsed) = meter.GetSummary();
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatusLog.Text = $"Received {FormatBytes(totalBytes)} in {elapsed:F1}s ({avgMBps:F1} MB/s). Saved to {destPath}";
                        MessageBox.Show($"File received successfully!\nSaved to: {destPath}\nSpeed: {avgMBps:F1} MB/s", "Transfer Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (OperationCanceledException)
                {
                    Dispatcher.Invoke(() => TxtStatusLog.Text = "Receiver stopped.");
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        TxtStatusLog.Text = $"Receiver error: {ex.Message}";
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isReceiverRunning = false;
                        BtnToggleReceiver.Content = "Start Receiver";
                        BtnToggleReceiver.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                    });
                }
            });
        }

        #endregion

        #region Live Speed Graph Canvas Rendering

        private void DrawGraph()
        {
            GraphCanvas.Children.Clear();
            double width = GraphCanvas.ActualWidth;
            double height = GraphCanvas.ActualHeight;

            if (width <= 10 || height <= 10 || _speedSamples.Count < 2) return;

            // Target 90 MB/s indicator line
            double maxSpeed = Math.Max(120.0, _speedSamples.Max() * 1.15);
            double targetY = height - (90.0 / maxSpeed * height);

            if (targetY >= 0 && targetY <= height)
            {
                var targetLine = new Line
                {
                    X1 = 0,
                    Y1 = targetY,
                    X2 = width,
                    Y2 = targetY,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669")),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 4 }
                };
                GraphCanvas.Children.Add(targetLine);
            }

            // Draw Throughput Curve
            var points = new PointCollection();
            double step = width / Math.Max(1, _speedSamples.Count - 1);

            for (int i = 0; i < _speedSamples.Count; i++)
            {
                double x = i * step;
                double speed = _speedSamples[i];
                double y = height - (speed / maxSpeed * height);
                points.Add(new Point(x, Math.Clamp(y, 2, height - 2)));
            }

            var polyline = new Polyline
            {
                Points = points,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8")),
                StrokeThickness = 2.5
            };

            GraphCanvas.Children.Add(polyline);
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HyperSend.Core;

namespace HyperSend.Benchmark
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=========================================================");
            Console.WriteLine(" HyperSend M2 Chunk Engine Benchmark & Sweep Utility");
            Console.WriteLine(" Targets: Phone -> Laptop >= 90 MB/s, Laptop -> Phone >= 65 MB/s");
            Console.WriteLine("=========================================================\n");

            if (args.Length == 0 || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
            {
                PrintUsage();
                return 0;
            }

            string mode = args[0].ToLowerInvariant();

            try
            {
                switch (mode)
                {
                    case "discover":
                        RunDiscover();
                        return 0;

                    case "server":
                    case "recv":
                    case "receive":
                        return await RunServerAsync(args);

                    case "client":
                    case "send":
                        return await RunClientAsync(args);

                    case "loopback":
                        return await RunLoopbackAsync(args);

                    default:
                        Console.WriteLine($"Unknown mode: {mode}");
                        PrintUsage();
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[ERROR] {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  HyperSend.Benchmark discover");
            Console.WriteLine("      Inspects active Wi-Fi link health and auto-detects phone hotspot IP.\n");
            Console.WriteLine("  HyperSend.Benchmark server [--port 5000] [--streams 4] [--chunk 2MB] [--engine chunk|raw] [--bytes 2GB] [--file <path>]");
            Console.WriteLine("      Starts receiver. Default engine is 'chunk' with dynamic work queue.\n");
            Console.WriteLine("  HyperSend.Benchmark client [--ip <phone-ip|auto>] [--port 5000] [--streams 4] [--chunk 2MB] [--engine chunk|raw] [--bytes 2GB] [--file <path>]");
            Console.WriteLine("      Starts sender to target IP.\n");
            Console.WriteLine("  HyperSend.Benchmark loopback [--bytes 1GB] [--streams 4] [--chunk 2MB]");
            Console.WriteLine("      Verifies local in-memory pipeline throughput ceiling with chunk engine.");
        }

        static void RunDiscover()
        {
            Console.WriteLine("[INFO] Querying active network adapters...");
            var gatewayIp = NetworkDiscovery.GetPhoneHotspotGatewayIp();
            if (gatewayIp != null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[FOUND] Phone Hotspot Gateway IPv4: {gatewayIp}");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARN] No active Wi-Fi gateway detected. Ensure laptop is connected to phone hotspot.");
                Console.ResetColor();
            }

            Console.WriteLine("\n[INFO] Querying WLAN adapter health:");
            var health = NetworkDiscovery.QueryLinkHealth();
            Console.WriteLine($"  SSID:         {health.Ssid}");
            Console.WriteLine($"  Radio Type:   {health.RadioType}");
            Console.WriteLine($"  Band:         {health.Band}");
            Console.WriteLine($"  Channel:      {health.Channel}");
            Console.WriteLine($"  Signal:       {health.SignalPercentage}%");
            Console.WriteLine($"  RX Link Rate: {health.ReceiveRateMbps} Mbps");
            Console.WriteLine($"  TX Link Rate: {health.TransmitRateMbps} Mbps");

            if (!health.IsOptimal5GHzAx)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[ADVICE] For peak speed (>90 MB/s):");
                Console.WriteLine("  - Set Phone Mobile Hotspot Band to 5 GHz");
                Console.WriteLine("  - Enable 'Support Wi-Fi 6' in Hotspot advanced settings");
                Console.WriteLine("  - Disable 'Wi-Fi sharing' and 'Power saving'");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[OPTIMAL] 5 GHz Wi-Fi 6 link verified!");
                Console.ResetColor();
            }
        }

        static async Task<int> RunServerAsync(string[] args)
        {
            int port = GetIntArg(args, "--port", NetworkConfig.DefaultPort);
            int streams = GetIntArg(args, "--streams", NetworkConfig.DefaultStreams);
            int chunkSize = (int)GetBytesArg(args, "--chunk", NetworkConfig.DefaultChunkSize);
            long bytes = GetBytesArg(args, "--bytes", 2L * 1024 * 1024 * 1024);
            string? filePath = GetStringArg(args, "--file", null);
            string engine = GetStringArg(args, "--engine", "chunk")!.ToLowerInvariant();

            Console.WriteLine($"[SERVER] Listening on 0.0.0.0:{port}");
            Console.WriteLine($"[SERVER] Engine:          {(engine == "chunk" ? "M2 Dynamic Chunk Work Queue" : "M1 Static TCP")}");
            Console.WriteLine($"[SERVER] Parallel Streams: {streams}");
            if (engine == "chunk")
                Console.WriteLine($"[SERVER] Chunk Size:       {chunkSize / (1024.0 * 1024.0):F1} MB");
            Console.WriteLine($"[SERVER] Target Bytes:     {bytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"[SERVER] Mode:             {(string.IsNullOrEmpty(filePath) ? "Synthetic Memory Drain (iperf3 equivalent)" : $"File Write ({filePath})")}");
            Console.WriteLine("Waiting for sender connections...");

            var progress = new Progress<TransferProgress>(PrintLiveProgress);
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

            ThroughputMeter meter;
            if (engine == "chunk")
            {
                var server = new ChunkedTcpReceiver(port, streams);
                meter = await server.RunReceiverAsync(filePath, bytes, chunkSize, progress, cts.Token);
            }
            else
            {
                var server = new RawTcpServer(port, streams);
                meter = await server.RunReceiverAsync(filePath, bytes, progress, cts.Token);
            }

            var (avgMBps, avgMbps, totalBytes, elapsed) = meter.GetSummary();

            Console.WriteLine("\n\n================ TRANSFER COMPLETE ================");
            Console.WriteLine($"Total Transferred: {totalBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"Elapsed Time:      {elapsed:F2} seconds");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Average Throughput: {avgMBps:F2} MB/s ({avgMbps:F2} Mbit/s)");
            Console.ResetColor();
            EvaluateResult(avgMBps, isPhoneToLaptop: true);
            Console.WriteLine("===================================================\n");

            return 0;
        }

        static async Task<int> RunClientAsync(string[] args)
        {
            string? ipArg = GetStringArg(args, "--ip", "auto");
            string targetIp;

            if (string.Equals(ipArg, "auto", StringComparison.OrdinalIgnoreCase))
            {
                var detected = NetworkDiscovery.GetPhoneHotspotGatewayIp();
                if (detected == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Could not auto-detect phone hotspot IP. Specify --ip <address> manually.");
                    Console.ResetColor();
                    return 1;
                }
                targetIp = detected.ToString();
                Console.WriteLine($"[AUTO-DETECT] Found phone hotspot IP: {targetIp}");
            }
            else
            {
                targetIp = ipArg!;
            }

            int port = GetIntArg(args, "--port", NetworkConfig.DefaultPort);
            int streams = GetIntArg(args, "--streams", NetworkConfig.DefaultStreams);
            int chunkSize = (int)GetBytesArg(args, "--chunk", NetworkConfig.DefaultChunkSize);
            long bytes = GetBytesArg(args, "--bytes", 2L * 1024 * 1024 * 1024);
            string? filePath = GetStringArg(args, "--file", null);
            string engine = GetStringArg(args, "--engine", "chunk")!.ToLowerInvariant();

            Console.WriteLine($"[CLIENT] Connecting to {targetIp}:{port} across {streams} parallel streams...");
            Console.WriteLine($"[CLIENT] Engine:          {(engine == "chunk" ? "M2 Dynamic Chunk Work Queue" : "M1 Static TCP")}");
            if (engine == "chunk")
                Console.WriteLine($"[CLIENT] Chunk Size:       {chunkSize / (1024.0 * 1024.0):F1} MB");
            Console.WriteLine($"[CLIENT] Target Bytes:     {bytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"[CLIENT] Mode:             {(string.IsNullOrEmpty(filePath) ? "Synthetic Memory Stream (iperf3 equivalent)" : $"File Read ({filePath})")}");

            var progress = new Progress<TransferProgress>(PrintLiveProgress);
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

            ThroughputMeter meter;
            if (engine == "chunk")
            {
                var client = new ChunkedTcpSender(targetIp, port, streams, chunkSize);
                meter = await client.RunSenderAsync(filePath, bytes, progress, cts.Token);
            }
            else
            {
                var client = new RawTcpClient(targetIp, port, streams);
                meter = await client.RunSenderAsync(filePath, bytes, progress, cts.Token);
            }

            var (avgMBps, avgMbps, totalBytes, elapsed) = meter.GetSummary();

            Console.WriteLine("\n\n================ TRANSFER COMPLETE ================");
            Console.WriteLine($"Total Transferred: {totalBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"Elapsed Time:      {elapsed:F2} seconds");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Average Throughput: {avgMBps:F2} MB/s ({avgMbps:F2} Mbit/s)");
            Console.ResetColor();
            EvaluateResult(avgMBps, isPhoneToLaptop: false);
            Console.WriteLine("===================================================\n");

            return 0;
        }

        static async Task<int> RunLoopbackAsync(string[] args)
        {
            int streams = GetIntArg(args, "--streams", NetworkConfig.DefaultStreams);
            int chunkSize = (int)GetBytesArg(args, "--chunk", NetworkConfig.DefaultChunkSize);
            long bytes = GetBytesArg(args, "--bytes", 1L * 1024 * 1024 * 1024);
            int port = 5006;

            Console.WriteLine($"[LOOPBACK] M2 Chunk Engine verification over 127.0.0.1:{port}");
            Console.WriteLine($"  Streams: {streams} | Chunk Size: {chunkSize / (1024.0 * 1024.0):F1} MB | Total: {bytes / (1024.0 * 1024.0):F0} MB");

            var receiver = new ChunkedTcpReceiver(port, streams);
            var receiverTask = Task.Run(async () => await receiver.RunReceiverAsync(null, bytes, chunkSize));

            await Task.Delay(150);

            var sender = new ChunkedTcpSender("127.0.0.1", port, streams, chunkSize);
            var meter = await sender.RunSenderAsync(null, bytes);

            await receiverTask;

            var (avgMBps, avgMbps, totalBytes, elapsed) = meter.GetSummary();
            Console.WriteLine("\n[LOOPBACK RESULT]");
            Console.WriteLine($"  Time:       {elapsed:F2} s");
            Console.WriteLine($"  Throughput: {avgMBps:F2} MB/s ({avgMbps:F2} Mbit/s)");

            if (avgMBps > 500)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  [PASS] Chunk work queue and out-of-order pipeline verified at saturation speed.");
                Console.ResetColor();
            }

            return 0;
        }

        static void PrintLiveProgress(TransferProgress p)
        {
            Console.Write($"\rProgress: {p.Percentage,5:F1}% | {p.BytesTransferred / (1024.0 * 1024.0),7:F1} MB | Live: {p.CurrentMBps,6:F1} MB/s | Avg: {p.AverageMBps,6:F1} MB/s | {p.ElapsedSeconds,4:F1}s  ");
        }

        static void EvaluateResult(double mbps, bool isPhoneToLaptop)
        {
            double target = isPhoneToLaptop ? 90.0 : 65.0;
            double ceiling = isPhoneToLaptop ? 110.0 : 78.0;

            if (mbps >= target)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] Target exceeded! ({mbps:F1} MB/s >= target {target:F1} MB/s, {mbps / ceiling * 100:F1}% of iperf3 ceiling).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[DIAGNOSTIC] Speed is {mbps:F1} MB/s (Target: {target:F1} MB/s, Ceiling: {ceiling:F1} MB/s).");
                Console.ResetColor();
            }
        }

        static int GetIntArg(string[] args, string name, int defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var val))
                    return val;
            }
            return defaultValue;
        }

        static string? GetStringArg(string[] args, string name, string? defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return defaultValue;
        }

        static long GetBytesArg(string[] args, string name, long defaultValue)
        {
            string? raw = GetStringArg(args, name, null);
            if (string.IsNullOrEmpty(raw)) return defaultValue;

            raw = raw.Trim().ToUpperInvariant();
            if (raw.EndsWith("GB"))
            {
                if (double.TryParse(raw.Substring(0, raw.Length - 2), out var gb))
                    return (long)(gb * 1024 * 1024 * 1024);
            }
            if (raw.EndsWith("MB"))
            {
                if (double.TryParse(raw.Substring(0, raw.Length - 2), out var mb))
                    return (long)(mb * 1024 * 1024);
            }
            if (long.TryParse(raw, out var bytes))
                return bytes;

            return defaultValue;
        }
    }
}

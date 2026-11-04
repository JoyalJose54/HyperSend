using System;
using System.Diagnostics;
using System.Threading;

namespace HyperSend.Core
{
    public class ThroughputMeter
    {
        private long _totalBytes;
        private readonly Stopwatch _stopwatch = new();
        private long _lastBytes;
        private double _lastTimeSec;
        private readonly object _lock = new();

        public void Start()
        {
            _totalBytes = 0;
            _lastBytes = 0;
            _lastTimeSec = 0;
            _stopwatch.Restart();
        }

        public void Stop()
        {
            _stopwatch.Stop();
        }

        public void AddBytes(long count)
        {
            Interlocked.Add(ref _totalBytes, count);
        }

        public long TotalBytes => Interlocked.Read(ref _totalBytes);

        public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

        public double AverageMegabytesPerSecond
        {
            get
            {
                var sec = ElapsedSeconds;
                if (sec <= 0.001) return 0;
                return (TotalBytes / (1024.0 * 1024.0)) / sec;
            }
        }

        public double InstantaneousMegabytesPerSecond
        {
            get
            {
                lock (_lock)
                {
                    var currentSec = ElapsedSeconds;
                    var currentBytes = TotalBytes;
                    var deltaSec = currentSec - _lastTimeSec;

                    if (deltaSec < 0.2) // avoid noise in micro-intervals
                    {
                        return AverageMegabytesPerSecond;
                    }

                    var deltaBytes = currentBytes - _lastBytes;
                    _lastTimeSec = currentSec;
                    _lastBytes = currentBytes;

                    return (deltaBytes / (1024.0 * 1024.0)) / deltaSec;
                }
            }
        }

        public double AverageMegabitsPerSecond => AverageMegabytesPerSecond * 8.0;

        public (double avgMBps, double avgMbps, long totalBytes, double elapsedSec) GetSummary()
        {
            return (AverageMegabytesPerSecond, AverageMegabitsPerSecond, TotalBytes, ElapsedSeconds);
        }
    }
}


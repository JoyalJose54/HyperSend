@echo off
title HyperSend Sender (M1)
echo Starting HyperSend Client to Phone Hotspot (Auto-detect IP, 4 parallel streams)...
"%~dp0bin\HyperSend.Benchmark.exe" client --ip auto --port 5000 --streams 4 --bytes 2GB
pause

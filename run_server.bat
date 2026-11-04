@echo off
title HyperSend Receiver (M1)
echo Starting HyperSend Server on Port 5000 (4 parallel streams)...
"%~dp0bin\HyperSend.Benchmark.exe" server --port 5000 --streams 4 --bytes 2GB
pause

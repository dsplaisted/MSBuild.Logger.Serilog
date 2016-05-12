@echo off
msbuild /logger:%~dp0MSBuild.Logger.Serilog\bin\Debug\MSBuild.Logger.Serilog.dll;verbosity=diag /noconlog %*
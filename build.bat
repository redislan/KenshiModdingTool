@echo off
rem Builds Kenshi Modding Tool into a single .exe (folder "dist")
where dotnet >nul 2>nul
if errorlevel 1 (
  echo The .NET SDK is not installed on this PC.
  echo Install it with this command, then run build.bat again:
  echo     winget install Microsoft.DotNet.SDK.8
  echo or download it here: https://aka.ms/dotnet/download
  pause
  exit /b 1
)
dotnet publish -c Release -o dist
if not exist "dist\KenshiModdingTool.exe" (
  echo.
  echo BUILD FAILED. See the error message above.
  pause
  exit /b 1
)
echo.
echo OK : dist\KenshiModdingTool.exe
pause

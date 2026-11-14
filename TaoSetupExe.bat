@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo ==================================================
echo   VXT Gop CAD - tao file cai dat Setup.exe
echo ==================================================

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [!] Chua co .NET SDK. Cai bang lenh:  winget install Microsoft.DotNet.SDK.8
  echo     hoac tai tai https://dotnet.microsoft.com/download/dotnet/8.0
  pause & exit /b 1
)

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LocalAppData%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
  echo [!] Chua co Inno Setup 6 (mien phi). Cai bang lenh:  winget install JRSoftware.InnoSetup
  echo     hoac tai tai https://jrsoftware.org/isdl.php  roi chay lai file nay.
  pause & exit /b 1
)

echo.
echo [1/3] Build plugin AutoCAD 2024...
dotnet build src\VXT.GopCAD\VXT.GopCAD.csproj -c Release || goto :loi

echo.
echo [2/3] Build add-in Revit 2025...
dotnet build src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.csproj -c Release || goto :loi

echo.
echo [3/3] Dong goi Setup.exe...
"%ISCC%" installer\VXT_GopCAD.iss || goto :loi

echo.
echo XONG! File cai dat nam trong thu muc Output\
explorer "%~dp0Output"
pause
exit /b 0

:loi
echo.
echo [LOI] Xem thong bao phia tren. Chup man hinh gui lai de sua.
pause
exit /b 1

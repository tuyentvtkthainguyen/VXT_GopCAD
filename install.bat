@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist bin\AutoCAD\VXT.GopCAD.Core.dll (echo Chua build. Chay build.bat truoc. & pause & exit /b 1)

echo === Cai plugin AutoCAD 2024 (tu nap khi mo AutoCAD) ===
set "ACAD=%APPDATA%\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle"
if not exist "%ACAD%\Contents" mkdir "%ACAD%\Contents"
copy /y bundle\PackageContents.xml "%ACAD%\" >nul
copy /y bin\AutoCAD\VXT.GopCAD.dll "%ACAD%\Contents\" >nul
copy /y bin\AutoCAD\VXT.GopCAD.Core.dll "%ACAD%\Contents\" >nul
echo   ^-^> %ACAD%

if not exist bin\Revit\VXT.RevitXuatCAD.dll goto :xong
echo === Cai add-in Revit 2025 ===
set "RVT=%APPDATA%\Autodesk\Revit\Addins\2025"
if not exist "%RVT%\VXT.RevitXuatCAD\AutoCAD" mkdir "%RVT%\VXT.RevitXuatCAD\AutoCAD"
copy /y src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.addin "%RVT%\" >nul
copy /y bin\Revit\VXT.RevitXuatCAD.dll "%RVT%\VXT.RevitXuatCAD\" >nul
copy /y bin\AutoCAD\VXT.GopCAD.Core.dll "%RVT%\VXT.RevitXuatCAD\AutoCAD\" >nul
echo   ^-^> %RVT%

:xong
echo.
echo CAI XONG. Dong va mo lai AutoCAD / Revit. Tim tab "VXT" tren Ribbon.
pause

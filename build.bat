@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Build plugin AutoCAD 2024 (VXT.GopCAD + VXT.GopCAD.Core) ===
dotnet build src\VXT.GopCAD\VXT.GopCAD.csproj -c Release || goto :loi
echo === Build add-in Revit 2025 (VXT.RevitXuatCAD) ===
dotnet build src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.csproj -c Release || goto :loi
echo.
echo BUILD XONG. Ket qua nam trong thu muc bin\. Chay install.bat de cai.
pause
exit /b 0
:loi
echo.
echo LOI BUILD - xem thong bao phia tren.
pause
exit /b 1

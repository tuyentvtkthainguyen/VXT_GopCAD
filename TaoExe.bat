@echo off
cd /d "%~dp0"
call :main > tao_exe_log.txt 2>&1
type tao_exe_log.txt
timeout /t 10
exit /b

:main
echo [1/3] Build plugin AutoCAD
dotnet build src\VXT.GopCAD\VXT.GopCAD.csproj -c Release -nologo -v q
echo KQ_ACAD=%ERRORLEVEL%
echo [2/3] Build add-in Revit
dotnet build src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.csproj -c Release -nologo -v q
echo KQ_REVIT=%ERRORLEVEL%
echo [3/3] Build bo cai Setup.exe
dotnet build installer\VXT.Setup\VXT.Setup.csproj -c Release -nologo -v q
echo KQ_SETUP=%ERRORLEVEL%
if exist "Output\setup_build\VXT_GopCAD_Setup_1.0.0.exe" (
  copy /y "Output\setup_build\VXT_GopCAD_Setup_1.0.0.exe" "Output\" >nul
  echo XONG: Output\VXT_GopCAD_Setup_1.0.0.exe
  explorer /select,"%~dp0Output\VXT_GopCAD_Setup_1.0.0.exe"
) else (
  echo LOI: khong tao duoc exe
)
exit /b

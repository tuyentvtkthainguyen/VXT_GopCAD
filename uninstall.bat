@echo off
rmdir /s /q "%APPDATA%\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle" 2>nul
del /q "%APPDATA%\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD.addin" 2>nul
rmdir /s /q "%APPDATA%\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD" 2>nul
echo Da go cai dat.
pause

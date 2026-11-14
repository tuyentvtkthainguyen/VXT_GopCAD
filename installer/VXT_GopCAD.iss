; Bộ cài VXT Gộp CAD – biên dịch bằng Inno Setup 6 (miễn phí: https://jrsoftware.org/isdl.php)
; Cài cho người dùng hiện tại, không cần quyền Administrator.

#define AppName "VXT Gop CAD"
#define AppVer  "1.0.0"

[Setup]
AppId={{A0469322-E0DA-46BC-816C-0E54457AB8EA}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=Vu Xuan Tuyen (VXT)
DefaultDirName={userappdata}\VXT\GopCAD
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\Output
OutputBaseFilename=VXT_GopCAD_Setup_{#AppVer}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayName={#AppName} {#AppVer}
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Cai ca AutoCAD 2024 va Revit 2025"
Name: "custom"; Description: "Tu chon"; Flags: iscustom

[Components]
Name: "acad";  Description: "Plugin AutoCAD 2024 - gop file DWG (lenh VXTGOP)"; Types: full custom
Name: "revit"; Description: "Add-in Revit 2025 - xuat sheet ra 1 file CAD"; Types: full custom

[Files]
; ---- AutoCAD: bundle tự nạp ----
Source: "..\bundle\PackageContents.xml";     DestDir: "{userappdata}\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle"; Components: acad; Flags: ignoreversion
Source: "..\bin\AutoCAD\VXT.GopCAD.dll";      DestDir: "{userappdata}\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle\Contents"; Components: acad; Flags: ignoreversion
Source: "..\bin\AutoCAD\VXT.GopCAD.Core.dll"; DestDir: "{userappdata}\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle\Contents"; Components: acad; Flags: ignoreversion
; ---- Revit 2025 ----
Source: "..\src\VXT.RevitXuatCAD\VXT.RevitXuatCAD.addin"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Components: revit; Flags: ignoreversion
Source: "..\bin\Revit\VXT.RevitXuatCAD.dll";  DestDir: "{userappdata}\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD"; Components: revit; Flags: ignoreversion
Source: "..\bin\AutoCAD\VXT.GopCAD.Core.dll"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD\AutoCAD"; Components: revit; Flags: ignoreversion
; ---- Hướng dẫn ----
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Autodesk\ApplicationPlugins\VXT_GopCAD.bundle"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\VXT.RevitXuatCAD"
Type: filesandordirs; Name: "{commonappdata}\VXT\XuatCAD\temp"

[Messages]
FinishedLabel=Da cai xong. Hay dong va mo lai AutoCAD / Revit, tim tab "VXT" tren Ribbon.

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DirExists(ExpandConstant('{commonpf64}\Autodesk\AutoCAD 2024')) and
     not DirExists(ExpandConstant('{commonpf64}\Autodesk\Revit 2025')) then
    Result := MsgBox('Khong thay AutoCAD 2024 hay Revit 2025 tren may nay. Van tiep tuc cai?',
                     mbConfirmation, MB_YESNO) = IDYES;
end;

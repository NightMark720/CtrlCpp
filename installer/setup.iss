; CtrlC++ 剪贴板历史 — Inno Setup 安装包脚本
; 编译：tools\innosetup\ISCC.exe installer\setup.iss

#define MyAppName "CtrlC++ 剪贴板历史"
#define MyAppVersion "1.4.0"
#define MyAppExeName "CtrlCpp.exe"

[Setup]
AppId={{9C6F7E52-4B1A-4E3D-8A77-2D5C9B0F1A63}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=CtrlC++
; lowest：免管理员权限，装到用户目录（%LOCALAPPDATA%\Programs\CtrlCpp）
PrivilegesRequired=lowest
DefaultDirName={autopf}\CtrlCpp
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=CtrlCpp-Setup-1.4.0
SetupIconFile=..\src\CtrlCpp\Assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CtrlCpp.exe
CloseApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式(&D)"; GroupDescription: "附加任务:"

[Files]
Source: "..\dist\CtrlCpp.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\README-使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; 安装即启用开机自启（程序内设置可关闭；卸载时自动删除）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CtrlCpp"; ValueData: """{app}\CtrlCpp.exe"""; Flags: uninsdeletevalue

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "立即运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
// 安装前结束正在运行的实例，避免文件占用
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  R: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM CtrlCpp.exe /F >nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, R);
  Sleep(500);
  Result := '';
end;

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM CtrlCpp.exe /F >nul 2>&1"; Flags: runhidden; RunOnceId: "KillCtrlCpp"

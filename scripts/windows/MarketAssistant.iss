; MarketAssistant Inno Setup 安装脚本
; 用于创建 Windows 安装程序
; 需要安装 Inno Setup 6.0+ : https://jrsoftware.org/isdl.php

#define MyAppName "Market Assistant"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "MarketAssistant Team"
#define MyAppURL "https://github.com/yourusername/MarketAssistant"
#define MyAppExeName "MarketAssistant.exe"
#define MyAppId "{{8A5B3C2D-9E1F-4A7B-8C6D-3E2F1A9B8C7D}"

[Setup]
; 应用基本信息
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
InfoBeforeFile=..\..\README.md
OutputDir=..\..\Release\Windows
OutputBaseFilename=MarketAssistant-Setup-{#MyAppVersion}
SetupIconFile=..\..\src\Assets\logo.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; 版本信息
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Installer
VersionInfoCopyright=Copyright (C) 2025 {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

; UI 配置
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "..\..\Release\Windows-x64\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\Release\Windows-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 注意: 不要在任何共享系统文件上使用"Flags: ignoreversion"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// 检查是否已安装
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  
  // 检查是否有旧版本正在运行
  if CheckForMutexes('MarketAssistant') then
  begin
    if MsgBox('Market Assistant 正在运行。是否关闭它并继续安装？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      Result := True;
    end
    else
    begin
      Result := False;
    end;
  end;
end;

// 卸载旧版本
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  
  // 提示用户
  if MsgBox('确定要卸载 Market Assistant 吗？', mbConfirmation, MB_YESNO) = IDYES then
  begin
    Result := True;
  end
  else
  begin
    Result := False;
  end;
end;

// 卸载完成后的清理
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 询问是否删除用户数据
    if MsgBox('是否删除应用数据和配置？', mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\MarketAssistant'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\MarketAssistant'), True, True, True);
    end;
  end;
end;


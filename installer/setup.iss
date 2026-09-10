; DLSSG SM86 管理器 —— 安装脚本（Inno Setup 6）
;
; 设计要点：
;
; 1. 安装路径可选。用户在向导里能改目录，也可以装到 C:\Program Files。
;
; 2. Mod 文件（约 75 MB）不随安装包分发，也不写入安装目录。原因是这些二进制
;    属于上游项目、授权不允许转发；而且安装目录可能不可写。程序检测到自身由
;    安装包部署（存在 unins*.exe）后，会统一把数据写到
;    %APPDATA%\DLSSGManager，见 ModSourceLocator.IsInstalledCopy。
;
; 3. 因此卸载只删除程序本体，游戏列表、备份和已下载的 Mod 文件都会保留，
;    重装后无需重新下载。是否清除由卸载时的询问决定。

#define AppName "DLSSG SM86 管理器"
#define AppShortName "DLSSGManager"
#define AppPublisher "BUNNY-19C"
#define AppUrl "https://github.com/BUNNY-19C/DLSS-30-"

; 版本号由构建命令传入，未传时用占位值。
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef SourceExe
  #define SourceExe "..\publish\DLSSGManager.exe"
#endif

[Setup]
AppId={{8F3A9C41-5D62-4E17-9B84-2C7F1A6E5D93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename={#AppShortName}-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 允许用户选择安装路径，包括非管理员可写的目录。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; 关闭系统还原点之外的改动；本安装包不改注册表关联。
UninstallDisplayIcon={app}\{#AppShortName}.exe
MinVersion=10.0
SetupLogging=yes
DisableWelcomePage=no
AllowNoIcons=yes

[Languages]
; 简体中文语言文件随仓库提供，因为 Inno Setup 安装包未内置它。
Name: "chinese"; MessagesFile: "languages\ChineseSimplified.isl"

[CustomMessages]
chinese.CreateDesktopIcon=创建桌面快捷方式
chinese.LaunchAfterInstall=安装完成后启动
chinese.DataDirNote=Mod 文件（约 75 MB）将在首次启动时下载到：%n%1%n这不会占用安装目录空间。
chinese.UninstallKeepData=保留我的游戏列表、备份和已下载的 Mod 文件
chinese.UninstallRemoveData=同时删除上述数据（不可恢复）

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; DestName: "{#AppShortName}.exe"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppShortName}.exe"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppShortName}.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppShortName}.exe"; Description: "{cm:LaunchAfterInstall}"; Flags: nowait postinstall skipifsilent

; 刻意没有 [UninstallDelete] 段：
;   · Mod 文件与用户数据都在 %APPDATA%\DLSSGManager，由下方代码按用户选择处理；
;   · 保留它们意味着重装后不必重新下载 75 MB；
;   · 安装目录只有程序本体，Inno 会自行清理。

[Code]
const
  { 与 ModSourceLocator.UserModDir 保持一致。 }
  UserModDirName = 'DLSSGManager\mod';
  UserDataDirName = 'DLSSGManager';

{ 首次启动时会被写入的目录，用于向用户说明数据位置。 }
function UserModDir(): string;
begin
  Result := ExpandConstant('{userappdata}\' + UserModDirName);
end;

function UserDataDir(): string;
begin
  Result := ExpandConstant('{userappdata}\' + UserDataDirName);
end;

procedure InitializeWizard();
var
  Info: TNewStaticText;
begin
  { 在目录选择页说明 Mod 文件的去向，避免用户以为安装目录会占 75 MB。 }
  Info := TNewStaticText.Create(WizardForm);
  Info.Parent := WizardForm.SelectDirPage;
  Info.Left := WizardForm.SelectDirLabel.Left;
  Info.Top := WizardForm.SelectDirLabel.Top + WizardForm.SelectDirLabel.Height + ScaleY(10);
  Info.Width := WizardForm.SelectDirLabel.Width;
  Info.AutoSize := False;
  Info.Height := ScaleY(52);
  Info.WordWrap := True;
  Info.Caption := FmtMessage(CustomMessage('DataDirNote'), [UserModDir()]);
end;

{ 记录用户的选择，供卸载时决定是否清除数据。 }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := UserDataDir();
    if DirExists(DataDir) then
    begin
      if UninstallSilent() then
      begin
        { 静默卸载默认保留数据，避免自动化场景里误删。 }
      end
      else if MsgBox('是否删除管理器数据？' + #13#10 + #13#10 +
                     DataDir + #13#10 + #13#10 +
                     '包含游戏列表、各游戏配置、被占用文件的备份，以及已下载的 Mod 文件。',
                     mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;

{ 安装前检查：目标目录已存在旧版本时提示覆盖（Inno 默认行为），此处只做可写性提示。 }
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;

  if CurPageID = wpSelectDir then
  begin
    { 让用户知道 Program Files 需要管理员权限，而数据目录则始终可写。 }
    if Pos(ExpandConstant('{autopf}'), WizardForm.DirEdit.Text) = 1 then
    begin
      { 安装到 Program Files 时，Inno 会自动请求提权，无需额外提示。 }
    end;
  end;
end;

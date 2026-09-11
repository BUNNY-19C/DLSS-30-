; DLSSG 30 系管理器 —— 安装脚本（Inno Setup 6）
;
; 设计要点：
;
; 1. 安装路径可选。用户在向导里能改目录，也可以装到 C:\Program Files。
;
; 2. Mod 文件（约 75 MB）不随安装包分发——这些二进制属于上游项目、授权不允许
;    转发——但**放在程序目录下**，让程序与其数据在一起。获取方式有两种：
;      · 勾选安装时的下载任务（默认勾选，见 [Tasks] 的 fetchmod）；
;      · 或首次启动时按提示下载。
;    程序自身由安装包部署时（存在 unins*.exe）走同一套路径选择逻辑，见
;    ModSourceLocator.ResolveTarget。
;
; 3. 在安装阶段下载有个实际好处：此时安装程序已提权，因此即使装到
;    Program Files，也能把 Mod 文件写进程序目录。若程序目录不可写，
;    运行时会自动改放到 %APPDATA%\DLSSGManager\mod。
;
; 4. 卸载时询问是否删除 Mod 文件与游戏数据。静默卸载默认全部保留，
;    避免自动化场景误删 75 MB 的下载。

#define AppName "DLSSG 30 系管理器"
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
; 升级安装时 Inno 的默认行为（DisableDirPage=auto）是跳过目录选择页、沿用上次
; 的路径，避免装出两份。但用户明确希望能改路径，所以强制显示该页。
; UsePreviousAppDir 保持默认的 yes：页面会预填上次的位置，仍可修改。
DisableDirPage=no
UsePreviousAppDir=yes
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
; 英语用 Inno Setup 内置的 Default.isl；简体中文语言文件随仓库提供，因为安装包未内置它。
; 向导启动时会显示语言选择框（ShowLanguageDialog=auto：多语言时自动显示）。
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinese"; MessagesFile: "languages\ChineseSimplified.isl"

[CustomMessages]
; ── 简体中文 ──────────────────────────────────────────────────────
chinese.CreateDesktopIcon=创建桌面快捷方式
chinese.LaunchAfterInstall=安装完成后启动
chinese.DataDirNote=Mod 文件（约 75 MB）将放在程序目录下的 mod\ 文件夹。%n若该位置不可写（例如安装到 Program Files 且未以管理员运行），会自动改放到：%n%1
chinese.ModFilesGroup=Mod 文件
chinese.FetchModFiles=安装时下载 Mod 文件到程序目录（约 75 MB，需要联网）
chinese.FetchPageTitle=正在获取 Mod 文件
chinese.FetchPageSubTitle=从 GitHub 下载 dlssg_for_sm86，请稍候
chinese.FetchPageText=下载并解压中，约 75 MB，通常需要一到两分钟…
chinese.FetchFailed=Mod 文件未能下载成功。%n%n程序仍可使用，首次启动时会再次提示，也可以稍后点「下载 / 更新 Mod 文件」重试。

; ── English ───────────────────────────────────────────────────────
english.CreateDesktopIcon=Create a desktop shortcut
english.LaunchAfterInstall=Launch after installation
english.DataDirNote=Mod files (about 75 MB) go into a mod\ folder beside the program.%nIf that location is not writable (for example, installed under Program Files without elevation), they are placed in:%n%1
english.ModFilesGroup=Mod files
english.FetchModFiles=Download the mod files into the program folder during setup (about 75 MB, needs a network connection)
english.FetchPageTitle=Downloading mod files
english.FetchPageSubTitle=Fetching dlssg_for_sm86 from GitHub, please wait
english.FetchPageText=Downloading and extracting, about 75 MB. This usually takes one to two minutes…
english.FetchFailed=The mod files could not be downloaded.%n%nThe program still works: it will offer to retry on first start, and you can also use “Download / update mod files” later.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "fetchmod"; Description: "{cm:FetchModFiles}"; GroupDescription: "{cm:ModFilesGroup}"

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

var
  { 目录选择页上的补充说明。位置在 CurPageChanged 里按最终布局计算。 }
  DataNoteLabel: TNewStaticText;

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
begin
  { 只创建，不定位：此时各控件的高度尚未按实际文本计算，
    在 InitializeWizard 里取 SelectDirLabel.Height 会拿到未定型的值，
    导致标签压在「点击下一步…」上方。定位改在 CurPageChanged 完成。 }
  DataNoteLabel := TNewStaticText.Create(WizardForm);
  { 与 DirEdit 同父容器，保证坐标可直接套用。 }
  DataNoteLabel.Parent := WizardForm.DirEdit.Parent;
  DataNoteLabel.AutoSize := False;
  DataNoteLabel.WordWrap := True;
  DataNoteLabel.Visible := False;
end;

procedure PositionDataNote();
var
  RightEdge: Integer;
begin
  DataNoteLabel.Caption := FmtMessage(CustomMessage('DataDirNote'), [UserModDir()]);

  DataNoteLabel.Left := WizardForm.DirEdit.Left;
  { 放在目录输入框下方——那里是空白区域，不会与任何内置控件重叠。 }
  DataNoteLabel.Top := WizardForm.DirEdit.Top + WizardForm.DirEdit.Height + ScaleY(12);

  { 宽度覆盖到「浏览」按钮右缘，并留出页面右边距。 }
  RightEdge := WizardForm.DirBrowseButton.Left + WizardForm.DirBrowseButton.Width;
  DataNoteLabel.Width := RightEdge - DataNoteLabel.Left;
  if DataNoteLabel.Width <= 0 then
    DataNoteLabel.Width := WizardForm.DirEdit.Width;

  { 按实际换行结果调整高度，避免文字被截断。 }
  WizardForm.AdjustLabelHeight(DataNoteLabel);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectDir then
  begin
    PositionDataNote();
    DataNoteLabel.Visible := True;
  end
  else
    DataNoteLabel.Visible := False;
end;

{ 调用程序自身的下载器。放在安装阶段做有个实际好处：此时安装程序已提权，
  因此即使装到 Program Files，也能把 Mod 文件写进程序目录。 }
procedure DownloadModFiles();
var
  ProgressPage: TOutputProgressWizardPage;
  ExePath: string;
  ResultCode: Integer;
begin
  ExePath := ExpandConstant('{app}\{#AppShortName}.exe');
  if not FileExists(ExePath) then
  begin
    MsgBox(CustomMessage('FetchFailed'), mbInformation, MB_OK);
    exit;
  end;

  ProgressPage := CreateOutputProgressPage(
    CustomMessage('FetchPageTitle'), CustomMessage('FetchPageSubTitle'));
  ProgressPage.SetText(CustomMessage('FetchPageText'), '');
  { 下载器不回报百分比，用不确定进度条避免误导。 }
  ProgressPage.SetProgress(0, 1);
  ProgressPage.Show();
  try
    if not Exec(ExePath, '--fetch --silent', ExpandConstant('{app}'),
                SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      ResultCode := -1;
  finally
    ProgressPage.Hide();
    ProgressPage.Free();
  end;

  if ResultCode <> 0 then
    MsgBox(CustomMessage('FetchFailed'), mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('fetchmod') then
    DownloadModFiles();
end;

{ 卸载时处理两类数据：
    · 程序目录下的 mod\ —— 运行期创建，Inno 不会自动清理；
    · %APPDATA% 里的游戏列表与备份。
  两者都询问后再删，静默卸载一律保留，避免自动化场景误删 75 MB 的下载。 }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ModDir: string;
  DataDir: string;
begin
  if CurUninstallStep <> usUninstall then exit;
  if UninstallSilent() then exit;

  ModDir := ExpandConstant('{app}\mod');
  if DirExists(ModDir) then
  begin
    if MsgBox('是否删除已下载的 Mod 文件？' + #13#10 + #13#10 +
              ModDir + #13#10 + #13#10 +
              '约 75 MB。删除后若重新安装，需要再次下载。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ModDir, True, True, True);
  end;

  DataDir := UserDataDir();
  if DirExists(DataDir) then
  begin
    if MsgBox('是否删除管理器数据？' + #13#10 + #13#10 +
              DataDir + #13#10 + #13#10 +
              '包含游戏列表、各游戏配置与被占用文件的备份。',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(DataDir, True, True, True);
  end;
end;


; Format Wheel Print plugin installer (Inno Setup 6).
;   iscc /DAppVersion=1.0.0 /DPublishDir="..\src\FormatWheel.Plugin.Print\bin\Release\net10.0-windows" installer\FormatWheel.Print.iss
; Output: dist\FormatWheel.Print-Setup-<version>.exe

#define AppName      "Format Wheel · печать"
#define AppExe       "FormatWheel.Print.dll"
#ifndef AppVersion
  #define AppVersion "1.0.3"
#endif
#ifndef PublishDir
  #define PublishDir "..\src\FormatWheel.Plugin.Print\bin\Release\net10.0-windows"
#endif

[Setup]
AppId={{C3F8A12B-4D5E-6F7A-8B9C-0D1E2F3A4B5C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Format Wheel
VersionInfoVersion={#AppVersion}

; current user, no admin - installer drops files into user profile only
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\FormatWheel\extensions\FormatWheel.Print
DisableDirPage=yes
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
ShowLanguageDialog=no
LanguageDetectionMethod=uilanguage

OutputDir=..\dist
OutputBaseFilename=FormatWheel.Print-Setup-{#AppVersion}
SetupIconFile=..\src\FormatWheel\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=no

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
en.InstallFolder=Plugin will be installed to:
ru.InstallFolder=Плагин будет установлен в:

[Files]
; плагин и его зависимости
Source: "{#PublishDir}\FormatWheel.Print.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\FormatWheel.Print.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\FormatWheel.Sdk.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\System.Management.dll"; DestDir: "{app}"; Flags: ignoreversion

; runtimes для System.Management (WMI)
Source: "{#PublishDir}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

; PDB опциональны — оставляем для отладки, не мешают работе
Source: "{#PublishDir}\FormatWheel.Print.pdb"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\FormatWheel.Sdk.pdb"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; ярлыка не нужно — плагин не запускается напрямую

[Code]

function InitializeSetup(): Boolean;
begin
  // плагин не является отдельным приложением - просто копируем файлы в папку расширений
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MsgBox('Плагин установлен.' + #13#10 +
           'Перезапустите Format Wheel, чтобы расширение загрузилось.' + #13#10 +
           'Печать появится в третьем меню колеса (Alt + Shift).',
           mbInformation, MB_OK);
end;

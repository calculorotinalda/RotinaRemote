; Script Inno Setup oficial para RotinaRemote
#define MyAppName "RotinaRemote"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "RotinaRemote Corp."
#define MyAppURL "https://rotinaremote.local"
#define MyAppExeName "RotinaRemote.exe"

[Setup]
AppId={{D37B4391-739F-4903-87B7-99E75691C3E2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
OutputBaseFilename=RotinaRemote-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\icon.ico

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 1. Encerrar instâncias do executável antes de desinstalar
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName} /T"; Flags: runhidden waituntilterminated; RunOnceId: "KillRotinaRemote"
; 2. Parar o serviço nativo do Windows caso esteja ativo
Filename: "sc.exe"; Parameters: "stop RotinaRemoteService"; Flags: runhidden waituntilterminated; RunOnceId: "StopRotinaService"
; 3. Apagar o registo do serviço nativo do Windows
Filename: "sc.exe"; Parameters: "delete RotinaRemoteService"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteRotinaService"

[UninstallDelete]
; Elimina ficheiros criados em tempo de execução (config.json, log.txt, identity.dat) e diretórios do programa
Type: filesandordirs; Name: "{app}\*"
Type: dirifempty; Name: "{app}"
Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"
Type: filesandordirs; Name: "{userappdata}\{#MyAppName}"
Type: filesandordirs; Name: "{commonappdata}\{#MyAppName}"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  AppDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    // 1. Forçar fecho imediato de qualquer processo RotinaRemote.exe
    Exec('taskkill.exe', '/F /IM {#MyAppExeName} /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 2. Parar o serviço Windows do RotinaRemote se estiver em execução
    Exec('sc.exe', 'stop RotinaRemoteService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // 3. Eliminar o serviço Windows do Service Control Manager
    Exec('sc.exe', 'delete RotinaRemoteService', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Pausa para libertar descritores de ficheiros
    Sleep(1000);
  end
  else if CurUninstallStep = usPostUninstall then
  begin
    // 4. Apagar completamente a pasta de instalação no Windows (Program Files)
    AppDir := ExpandConstant('{app}');
    if DirExists(AppDir) then
    begin
      DelTree(AppDir, True, True, True);
    end;

    // 5. Apagar pastas residuais de utilizador se existirem
    if DirExists(ExpandConstant('{localappdata}\{#MyAppName}')) then
      DelTree(ExpandConstant('{localappdata}\{#MyAppName}'), True, True, True);
    if DirExists(ExpandConstant('{userappdata}\{#MyAppName}')) then
      DelTree(ExpandConstant('{userappdata}\{#MyAppName}'), True, True, True);
  end;
end;

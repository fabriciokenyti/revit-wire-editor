; ============================================================================
;  RevitWireEditor - Instalador (Inno Setup 6)
;  ---------------------------------------------------------------------------
;  Instala o bundle em:
;    %APPDATA%\Autodesk\ApplicationPlugins\RevitWireEditor.bundle
;  Instalacao POR USUARIO (nao requer privilegios de administrador).
;
;  O Revit le o PackageContents.xml dessa pasta na inicializacao e carrega
;  a versao correta (2026 ou 2027).
;
;  Pre-requisito: buildar os DOIS targets antes de compilar o instalador:
;    dotnet build RevitWireEditor\RevitWireEditor.csproj -c R2026
;    dotnet build RevitWireEditor\RevitWireEditor.csproj -c R2027
;  (o PostBuild popula RevitWireEditor.bundle\Contents\2026 e \2027)
;
;  Compilar:  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" RevitWireEditor.iss
;  Saida:     installer\Output\RevitWireEditor-1.0.0-Setup.exe
; ============================================================================

#define MyAppName "RevitWireEditor"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Engenharia Moderna"
#define MyAppEmail "fabricio@engenhariamoderna.com.br"
#define BundleName "RevitWireEditor.bundle"
; Caminho do bundle relativo a este .iss (installer\ -> raiz do repo)
#define BundleSource "..\RevitWireEditor.bundle"

[Setup]
; AppId reutiliza o UpgradeCode do PackageContents.xml -> atualizacoes in-place.
AppId={{8F3A2C10-5D4B-4E7A-9C21-7B0E1A2D3F94}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppContact={#MyAppEmail}
AppPublisherURL=mailto:{#MyAppEmail}

; ----- Instalacao por usuario (sem admin) -----
PrivilegesRequired=lowest
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\{#BundleName}
UsePreviousAppDir=yes
DisableDirPage=yes
DisableProgramGroupPage=yes

; Mantem o executavel de desinstalacao FORA da pasta .bundle
UninstallFilesDir={userappdata}\{#MyAppName}\uninstall
UninstallDisplayName={#MyAppName} {#MyAppVersion}

; ----- Saida -----
OutputDir=Output
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
; Copia o bundle inteiro (PackageContents.xml + Contents\2026 e \2027),
; exceto marcadores de repositorio e simbolos de debug.
Source: "{#BundleSource}\*"; DestDir: "{app}"; \
    Excludes: "*.gitkeep,*.pdb"; \
    Flags: recursesubdirs createallsubdirs ignoreversion

[Code]
// Avisa (sem bloquear) se o Revit estiver aberto: os DLLs podem estar
// travados e uma atualizacao pode falhar em copiar por cima.
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;
  if Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq Revit.exe" | find /I "Revit.exe"',
          '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
      MsgBox('O Revit parece estar aberto.' + #13#10 +
             'Se esta ATUALIZANDO o plugin, feche o Revit antes de continuar ' +
             'para evitar erro ao copiar os arquivos.',
             mbInformation, MB_OK);
  end;
end;

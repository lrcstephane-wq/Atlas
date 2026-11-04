#ifndef AtlasVersion
  #define AtlasVersion "0.20.0"
#endif

#ifndef PayloadDir
  #define PayloadDir "..\publish\portable"
#endif

#ifndef OutputDir
  #define OutputDir "..\publish"
#endif

[Setup]
AppId={{6273D7A2-B24E-48E8-A8C5-03C2DB747A60}
AppName=Biblidéo Atlas
AppVersion={#AtlasVersion}
AppPublisher=Idéo Solutions
DefaultDirName={sd}\Ideo_Atlas\Application
DisableDirPage=yes
DefaultGroupName=Idéo Solutions
DisableProgramGroupPage=yes
Uninstallable=yes
UninstallDisplayName=Biblidéo Atlas
UninstallDisplayIcon={app}\Atlas.exe
UninstallFilesDir={sd}\Ideo_Atlas\Uninstall
OutputDir={#OutputDir}
OutputBaseFilename=Atlas-Setup-{#AtlasVersion}-win-x64
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AtlasVersion}
VersionInfoCompany=Idéo Solutions
VersionInfoDescription=Installation de Biblidéo Atlas
VersionInfoProductName=Biblidéo Atlas
VersionInfoProductVersion={#AtlasVersion}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Dirs]
Name: "{sd}\Ideo_Atlas\Configuration"; Permissions: users-modify
Name: "{sd}\Ideo_Atlas\Cache"; Permissions: users-modify
Name: "{sd}\Ideo_Atlas\Logs"; Permissions: users-modify
Name: "{sd}\Ideo_Atlas\MisesAJour"; Permissions: users-modify
Name: "{sd}\Ideo_Atlas\Donnees"; Permissions: users-modify
Name: "{commonappdata}\Ideo Solutions\Atlas"; Permissions: users-modify

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commondesktop}\Biblidéo Atlas"; Filename: "{app}\Atlas.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Atlas.exe"
Name: "{group}\Biblidéo Atlas"; Filename: "{app}\Atlas.exe"; WorkingDir: "{app}"; IconFilename: "{app}\Atlas.exe"
Name: "{group}\Désinstaller Biblidéo Atlas"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\Atlas.exe"; Description: "Lancer Biblidéo Atlas"; Flags: nowait postinstall skipifsilent

[Code]
var
  RemoveAllLocalData: Boolean;

procedure WriteDeploymentPolicy;
var
  PolicyPath: String;
  DeploymentMode: String;
  Json: String;
begin
  PolicyPath := ExpandConstant('{sd}\Ideo_Atlas\Configuration\deployment.json');
  if FileExists(PolicyPath) then
    exit;

  DeploymentMode := Lowercase(ExpandConstant('{param:ATLASMODE|production}'));
  if DeploymentMode <> 'development' then
    DeploymentMode := 'production';

  ForceDirectories(ExtractFileDir(PolicyPath));
  Json := '{' + #13#10 +
    '  "SchemaVersion": 1,' + #13#10 +
    '  "Mode": "' + DeploymentMode + '"' + #13#10 +
    '}' + #13#10;
  SaveStringToFile(PolicyPath, Json, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    WriteDeploymentPolicy;
end;

function InitializeUninstall(): Boolean;
var
  Choice: Integer;
begin
  Choice := MsgBox(
    'Souhaitez-vous également supprimer toutes les données locales Atlas de ce poste ?' + #13#10 + #13#10 +
    'OUI : suppression complète de l''application, des paramètres locaux, des comptes, de la licence et des données présentes dans C:\Ideo_Atlas.' + #13#10 + #13#10 +
    'NON : désinstallation de l''application uniquement. Les données et la licence seront conservées pour une réinstallation.' + #13#10 + #13#10 +
    'Les dossiers réseau externes ne sont jamais supprimés.',
    mbConfirmation, MB_YESNOCANCEL);

  if Choice = IDCANCEL then
  begin
    Result := False;
    exit;
  end;

  RemoveAllLocalData := Choice = IDYES;
  if RemoveAllLocalData then
  begin
    Result := MsgBox(
      'La suppression complète est irréversible pour les données locales.' + #13#10 +
      'Confirmez-vous leur suppression définitive ?',
      mbError, MB_YESNO) = IDYES;
    exit;
  end;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemoveAllLocalData then
  begin
    DelTree(ExpandConstant('{commonappdata}\Ideo Solutions\Atlas'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\Ideo Solutions\Atlas'), True, True, True);
    DelTree(ExpandConstant('{sd}\Ideo_Atlas'), True, True, True);
  end;
end;

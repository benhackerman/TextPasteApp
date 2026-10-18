; Instalador de TextPaste (Inno Setup 6). Lo compila GitHub Actions; no hace falta tocarlo.
; Se instala para el usuario actual en %LocalAppData%\Programs\TextPaste: no pide permisos de administrador.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{8E2B7C1A-5F3D-4C8E-9A61-2D7B4E0F9C13}
AppName=TextPaste
AppVersion={#MyAppVersion}
AppVerName=TextPaste {#MyAppVersion}
AppPublisher=benhackerman
AppPublisherURL=https://github.com/benhackerman/TextPasteApp
DefaultDirName={localappdata}\Programs\TextPaste
DefaultGroupName=TextPaste
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..\out
OutputBaseFilename=TextPaste-{#MyAppVersion}-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\TextPasteApp.exe
UninstallDisplayName=TextPaste
WizardStyle=modern

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "startup"; Description: "Iniciar TextPaste con Windows"; GroupDescription: "Opciones:"
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked

[Files]
Source: "..\publish\TextPasteApp.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TextPaste"; Filename: "{app}\TextPasteApp.exe"
Name: "{userdesktop}\TextPaste"; Filename: "{app}\TextPasteApp.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TextPasteApp"; \
    ValueData: """{app}\TextPasteApp.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
; También corre en las actualizaciones silenciosas: así la app vuelve a abrirse sola.
Filename: "{app}\TextPasteApp.exe"; Description: "Abrir TextPaste"; Flags: nowait postinstall

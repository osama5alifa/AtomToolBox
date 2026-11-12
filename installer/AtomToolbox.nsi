Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!define APPNAME "AtomToolBox"
!define VERSION "0.2.5"
!define PUBLISHER "Project Atom"
!define APPEXE "Atomtoolbox.exe"

!ifndef PAYLOAD
  !define PAYLOAD "..\publish\self-contained"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\publish\AtomToolbox-Setup-${VERSION}.exe"
!endif

Name "${APPNAME} ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation"
RequestExecutionLevel admin
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${APPNAME} ${VERSION}"

VIProductVersion "0.2.5.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "FileDescription" "${APPNAME} installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "MIT"
VIAddVersionKey "CompanyName" "${PUBLISHER}"

!define MUI_ICON "..\src\AtomToolbox\Assets\app.ico"
!define MUI_UNICON "..\src\AtomToolbox\Assets\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APPEXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Run AtomToolBox"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Section "Install"
  SetShellVarContext all

  ; Close the app and the old Electron build before replacing the folder.
  ExecWait '"$SYSDIR\taskkill.exe" /F /IM Atomtoolbox.exe /T'
  ExecWait '"$SYSDIR\taskkill.exe" /F /IM AtomToolBox.exe /T'
  Sleep 500

  ; If the old directory cannot be replaced, move it aside rather than mixing files.
  IfFileExists "$INSTDIR\Atomtoolbox.exe" 0 no_old_install
  ClearErrors
  Rename "$INSTDIR" "$INSTDIR.old"
  IfErrors 0 no_old_install
    MessageBox MB_ICONSTOP "Could not replace $INSTDIR.$\r$\nClose AtomToolBox (including the old 0.2.0 app), then run the installer again."
    Abort
  no_old_install:

  ; Remove the old Electron uninstall registration.
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox 0.2.0"
  DeleteRegKey HKLM "Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox 0.2.0"

  CreateDirectory "$INSTDIR"
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${APPEXE}" "" "$INSTDIR\${APPEXE}" 0

  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayName" "${APPNAME}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "DisplayIcon" "$INSTDIR\${APPEXE},0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "NoRepair" 1
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "InstallDate" ""
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  Delete "$DESKTOP\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPNAME}"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox 0.2.0"
  RMDir /r "$INSTDIR"
SectionEnd

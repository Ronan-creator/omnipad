; Omnipad installer (NSIS 3).
; Build:  makensis -DVERSION=1.8.0 -DSRC=<folder with the built app> -DVIGEM=<ViGEmBus setup exe> omnipad.nsi
; Installs for the current user (no admin needed), so Omnipad can keep its settings and log next to the exe.

Unicode true
!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef SRC
  !define SRC "..\Omnipad\bin\Release\net48"
!endif
!ifndef VIGEM
  !define VIGEM "ViGEmBus_Setup.exe"
!endif

Name "Omnipad"
OutFile "Omnipad-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\Omnipad"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "Omnipad ${VERSION} by Omnific"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "Omnipad"
VIAddVersionKey "CompanyName" "Omnific"
VIAddVersionKey "FileDescription" "Omnipad installer"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "LegalCopyright" "Omnific. Based on BetterJoy by Davidobot (MIT)."

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!define MUI_ICON "..\Omnipad\Icons\omnipad.ico"
!define MUI_UNICON "..\Omnipad\Icons\omnipad.ico"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "Omnipad ${VERSION}"
!define MUI_WELCOMEPAGE_TEXT "Use your Nintendo Switch Pro Controller and Joy-Cons as Xbox controllers on Windows - over USB or Bluetooth.$\r$\n$\r$\nSetup installs Omnipad for your account and, if needed, the ViGEmBus driver it uses to create the Xbox controller.$\r$\n$\r$\nClick Next to continue."
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Start Omnipad now (sets it up to start with Windows)"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchOmnipad

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Omnipad"

Section "Omnipad" SecMain
  ; Close a running copy so its files can be replaced.
  nsExec::Exec 'taskkill /IM Omnipad.exe /F'
  Sleep 500

  SetOutPath "$INSTDIR"
  File /x "*.pdb" /x "*.config" /x "settings" /x "*.log" "${SRC}\*.*"
  ; Keep existing settings on upgrade (they live in Omnipad.exe.config).
  SetOverwrite off
  File "${SRC}\Omnipad.exe.config"
  SetOverwrite on
  File "/oname=LICENSE.txt" "..\LICENSE"
  File "/oname=README.txt" "..\README.md"

  CreateShortcut "$SMPROGRAMS\Omnipad.lnk" "$INSTDIR\Omnipad.exe" "" "$INSTDIR\Omnipad.exe" 0

  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "Omnipad"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "Omnific"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\Omnipad.exe"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout" "https://github.com/Ronan-creator/omnipad"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ; ViGEmBus creates the virtual Xbox controller. Install it if it's missing.
  SetRegView 64
  ReadRegStr $0 HKLM "SYSTEM\CurrentControlSet\Services\ViGEmBus" "ImagePath"
  SetRegView 32
  ${If} $0 == ""
    MessageBox MB_OKCANCEL|MB_ICONINFORMATION "Omnipad needs the ViGEmBus driver to create the Xbox controller.$\r$\n$\r$\nClick OK to install it now (Windows will ask for permission)." IDCANCEL skipvigem
    InitPluginsDir
    File "/oname=$PLUGINSDIR\ViGEmBus_Setup.exe" "${VIGEM}"
    ExecShellWait "runas" "$PLUGINSDIR\ViGEmBus_Setup.exe" "/passive /norestart"
    skipvigem:
  ${EndIf}
SectionEnd

; First start runs as administrator once, so Omnipad can register itself to start with Windows
; with full rights (no prompt at every sign-in).
Function LaunchOmnipad
  ExecShell "runas" "$INSTDIR\Omnipad.exe"
FunctionEnd

Section "Uninstall"
  nsExec::Exec 'taskkill /IM Omnipad.exe /F'
  Sleep 500

  ; Remove the start-up entries. The scheduled task was created with admin rights, so removing it needs them too.
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "Omnipad"
  nsExec::Exec 'schtasks /Query /TN "Omnipad"'
  Pop $0
  ${If} $0 == 0
    ExecShellWait "runas" "schtasks.exe" '/Delete /TN "Omnipad" /F' SW_HIDE
  ${EndIf}

  Delete "$SMPROGRAMS\Omnipad.lnk"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "${UNINST_KEY}"
SectionEnd

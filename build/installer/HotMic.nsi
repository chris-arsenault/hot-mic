!include "MUI2.nsh"

!ifndef PRODUCT_NAME
  !define PRODUCT_NAME "HotMic"
!endif

!ifndef PUBLISHER
  !define PUBLISHER "HotMic"
!endif

!ifndef WEBSITE
  !define WEBSITE "https://github.com/chris-arsenault/hot-mic"
!endif

!ifndef APP_VERSION
  !define APP_VERSION "0.0.0"
!endif

!ifndef APP_FILE_VERSION
  !define APP_FILE_VERSION "0.0.0.0"
!endif

!ifndef APP_EXE
  !define APP_EXE "HotMic.App.exe"
!endif

!ifndef OUTPUT_NAME
  !define OUTPUT_NAME "HotMic-Setup.exe"
!endif

!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "publish\app"
!endif

Name "${PRODUCT_NAME}"
OutFile "${OUTPUT_NAME}"
InstallDir "$LOCALAPPDATA\HotMic"
RequestExecutionLevel user
!define MUI_ICON "${PUBLISH_DIR}\Assets\hotmic.ico"
!define MUI_UNICON "${PUBLISH_DIR}\Assets\hotmic.ico"

VIProductVersion "${APP_FILE_VERSION}"
VIAddVersionKey "ProductName" "${PRODUCT_NAME}"
VIAddVersionKey "Publisher" "${PUBLISHER}"
VIAddVersionKey "FileDescription" "HotMic Windows Installer"
VIAddVersionKey "FileVersion" "${APP_FILE_VERSION}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"

!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

!define UNINSTALL_REG_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${PRODUCT_NAME}"

Function CheckDotNet10
  EnumRegKey $0 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" 0
  StrCmp $0 "" noDotNet
  StrCpy $1 $0 3
  StrCmp $1 "10." dotnetFound noDotNet

  noDotNet:
  MessageBox MB_YESNO|MB_ICONEXCLAMATION "${PRODUCT_NAME} requires the .NET 10 Desktop Runtime, which was not detected.$\n$\nOpen the download page now?" IDNO continueInstall
  ExecShell "open" "https://dotnet.microsoft.com/en-us/download/dotnet/10.0"

  continueInstall:
  Return

  dotnetFound:
  Return
FunctionEnd

Function .onInit
  Call CheckDotNet10
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_DIR}\*.*"
  CreateShortcut "$DESKTOP\HotMic.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\HotMic.lnk" "$INSTDIR\${APP_EXE}"
  WriteUninstaller "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayName" "${PRODUCT_NAME}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_REG_KEY}" "URLInfoAbout" "${WEBSITE}"
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_REG_KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  DeleteRegKey HKCU "${UNINSTALL_REG_KEY}"
  RMDir /r "$INSTDIR"
  Delete "$DESKTOP\HotMic.lnk"
  Delete "$SMPROGRAMS\HotMic.lnk"
SectionEnd

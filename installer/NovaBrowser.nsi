Unicode true
ManifestDPIAware true
XPStyle on
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
CRCCheck on
AllowRootDirInstall false

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

!define APP_NAME "Nova Browser"
!define APP_VERSION "1.0.0"
!define APP_EXE "NovaBrowser.exe"
!define APP_PUBLISHER "Nova Browser"
!define APP_KEY "Software\NovaBrowser"
!define UNINST_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\NovaBrowser"
!define WEBVIEW2_GUID "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "Output\NovaBrowser-Setup-1.0.0.exe"
InstallDir "$LOCALAPPDATA\Programs\NovaBrowser"
InstallDirRegKey HKCU "${APP_KEY}" "InstallDir"
BrandingText "${APP_NAME} ${APP_VERSION}"
Icon "assets\nova.ico"
UninstallIcon "assets\nova.ico"

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "${APP_NAME}"
VIAddVersionKey "ProductVersion" "${APP_VERSION}"
VIAddVersionKey "FileDescription" "Установщик Nova Browser"
VIAddVersionKey "FileVersion" "${APP_VERSION}"
VIAddVersionKey "LegalCopyright" "© 2026 Nova Browser"
VIAddVersionKey "CompanyName" "${APP_PUBLISHER}"

!define MUI_ABORTWARNING
!define MUI_ICON "assets\nova.ico"
!define MUI_UNICON "assets\nova.ico"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP "assets\header.bmp"
!define MUI_WELCOMEFINISHPAGE_BITMAP "assets\welcome.bmp"

!define MUI_WELCOMEPAGE_TITLE "Установка Nova Browser"
!define MUI_WELCOMEPAGE_TEXT "Установщик установит Nova Browser — быстрый браузер на движке Chromium.$\r$\n$\r$\nПрограмма устанавливается только для текущего пользователя и не требует прав администратора.$\r$\n$\r$\nНажмите «Далее», чтобы продолжить."

!define MUI_LICENSEPAGE_TEXT_TOP "Пожалуйста, прочитайте лицензионное соглашение. Для установки необходимо принять его условия."
!define MUI_LICENSEPAGE_TEXT_BOTTOM "Если вы не согласны с условиями, нажмите «Отмена»."
!define MUI_LICENSEPAGE_BUTTON "Принимаю"

!define MUI_DIRECTORYPAGE_TEXT_TOP "Выберите папку для установки Nova Browser. Рекомендуется оставить путь по умолчанию."

!define MUI_FINISHPAGE_TITLE "Nova Browser установлен"
!define MUI_FINISHPAGE_TEXT "Nova Browser успешно установлен. Ярлык добавлен на рабочий стол и в меню «Пуск».$\r$\n$\r$\nДля отображения сайтов используется компонент Microsoft Edge WebView2."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "Запустить Nova Browser"
!define MUI_FINISHPAGE_LINK "Открыть папку установки"
!define MUI_FINISHPAGE_LINK_LOCATION "$INSTDIR"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "assets\license.rtf"
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!define MUI_UNCONFIRMPAGE_TEXT_TOP "Программа Nova Browser будет удалена с этого компьютера."
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "Russian"

Function .onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Function un.onInit
  SetShellVarContext current
  SetRegView 64
FunctionEnd

Function EnsureWebView2
  ReadRegStr $0 HKLM "SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${If} $0 == ""
    ReadRegStr $0 HKLM "SOFTWARE\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${EndIf}
  ${If} $0 == ""
    ReadRegStr $0 HKCU "Software\Microsoft\EdgeUpdate\Clients\${WEBVIEW2_GUID}" "pv"
  ${EndIf}

  ${If} $0 != ""
    DetailPrint "Microsoft Edge WebView2 Runtime: $0"
    Return
  ${EndIf}

  DetailPrint "Microsoft Edge WebView2 Runtime не найден."
  DetailPrint "Устанавливаю WebView2 Runtime..."
  InitPluginsDir
  File /oname=$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe "assets\MicrosoftEdgeWebview2Setup.exe"
  ExecWait '"$PLUGINSDIR\MicrosoftEdgeWebview2Setup.exe" /silent /install' $1
  ${If} $1 != 0
    MessageBox MB_ICONEXCLAMATION|MB_OK "Не удалось автоматически установить компонент Microsoft Edge WebView2 (код $1).$\r$\n$\r$\nУстановите его вручную с сайта Microsoft, иначе страницы в браузере не будут открываться."
  ${Else}
    DetailPrint "WebView2 Runtime установлен."
  ${EndIf}
FunctionEnd

Section "Nova Browser" SecMain
  SectionIn RO
  SetShellVarContext current
  SetRegView 64
  SetOutPath "$INSTDIR"
  SetOverwrite on

  File "..\NovaBrowser\bin\Release\net8.0-windows\win-x64\publish\${APP_EXE}"
  File "..\NovaBrowser\LICENSE.txt"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Nova Browser"
  CreateShortcut "$SMPROGRAMS\Nova Browser\Nova Browser.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0
  CreateShortcut "$SMPROGRAMS\Nova Browser\Удалить Nova Browser.lnk" "$INSTDIR\uninstall.exe"
  CreateShortcut "$DESKTOP\Nova Browser.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0

  WriteRegStr HKCU "${APP_KEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"

  Call EnsureWebView2
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  SetRegView 64

  Delete "$DESKTOP\Nova Browser.lnk"
  Delete "$SMPROGRAMS\Nova Browser\Nova Browser.lnk"
  Delete "$SMPROGRAMS\Nova Browser\Удалить Nova Browser.lnk"
  RMDir "$SMPROGRAMS\Nova Browser"

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\LICENSE.txt"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"

  DeleteRegKey HKCU "${UNINST_KEY}"
  DeleteRegKey HKCU "${APP_KEY}"

  IfSilent NoData
  MessageBox MB_YESNO|MB_ICONQUESTION "Удалить также историю, закладки, настройки и данные сайтов Nova Browser?" IDNO NoData
  RMDir /r "$LOCALAPPDATA\NovaBrowser"
  RMDir /r "$TEMP\NovaBrowserPrivate"
  NoData:
SectionEnd

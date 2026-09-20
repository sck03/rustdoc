!include FileFunc.nsh
!include LogicLib.nsh
Var ExportDocManagerInstallDrive
Var ExportDocManagerSystemDrive

!macro EXPORTDOCMANAGER_WARN_IF_SYSTEM_DRIVE_INSTALL
  ${GetRoot} "$WINDIR" $ExportDocManagerSystemDrive
  ${GetRoot} "$INSTDIR" $ExportDocManagerInstallDrive

  ${If} "$ExportDocManagerSystemDrive" != ""
  ${AndIf} "$ExportDocManagerInstallDrive" == "$ExportDocManagerSystemDrive"
    IfSilent system_drive_install_notice_done
    MessageBox MB_ICONINFORMATION|MB_OK "当前程序将安装在 Windows 系统盘。首次启动时会要求选择独立的业务数据目录；如有非系统盘，建议优先选择。安装器不会在程序目录预创建数据库或运行数据。"
    system_drive_install_notice_done:
  ${EndIf}
!macroend

!macro NSIS_HOOK_PREINSTALL
  !insertmacro EXPORTDOCMANAGER_WARN_IF_SYSTEM_DRIVE_INSTALL
!macroend

!macro NSIS_HOOK_POSTINSTALL
!macroend

!macro NSIS_HOOK_PREUNINSTALL
!macroend

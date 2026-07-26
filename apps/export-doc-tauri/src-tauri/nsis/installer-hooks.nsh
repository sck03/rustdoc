!include FileFunc.nsh
!include LogicLib.nsh
!include StrFunc.nsh
${Using:StrFunc} StrRep

Var ExportDocManagerInstallDrive
Var ExportDocManagerSystemDrive
Var ExportDocManagerDataRoot
Var ExportDocManagerDataRootJson
Var ExportDocManagerRuntimePathsConfig

!macro EXPORTDOCMANAGER_WARN_IF_SYSTEM_DRIVE_INSTALL
  ${GetRoot} "$WINDIR" $ExportDocManagerSystemDrive
  ${GetRoot} "$INSTDIR" $ExportDocManagerInstallDrive

  ${If} "$ExportDocManagerSystemDrive" != ""
  ${AndIf} "$ExportDocManagerInstallDrive" == "$ExportDocManagerSystemDrive"
    IfSilent system_drive_install_notice_done
    MessageBox MB_ICONINFORMATION|MB_OK "当前安装目录位于 Windows 系统盘。程序会将可写数据放在安装目录下的 App_Data；如果设备有独立数据盘，可在首次运行时再设置数据目录。"
    system_drive_install_notice_done:
  ${EndIf}
!macroend

!macro EXPORTDOCMANAGER_CREATE_RUNTIME_DATA_ROOT
  StrCpy $ExportDocManagerDataRoot "$INSTDIR\App_Data"

  ClearErrors
  CreateDirectory "$ExportDocManagerDataRoot"
  CreateDirectory "$ExportDocManagerDataRoot\Database"
  CreateDirectory "$ExportDocManagerDataRoot\SingleWindow"
  CreateDirectory "$ExportDocManagerDataRoot\Backups"
  CreateDirectory "$ExportDocManagerDataRoot\Cache"
  IfErrors 0 runtime_data_root_created
    Abort "无法在 $ExportDocManagerDataRoot 创建外贸业务综合管理系统运行数据目录。"

  runtime_data_root_created:
!macroend

!macro EXPORTDOCMANAGER_WRITE_RUNTIME_PATHS_CONFIG
  StrCpy $ExportDocManagerRuntimePathsConfig "$INSTDIR\runtime-paths.json"
  IfFileExists "$ExportDocManagerRuntimePathsConfig" runtime_paths_config_done

  StrCpy $ExportDocManagerDataRoot "$INSTDIR\App_Data"
  ${StrRep} $ExportDocManagerDataRootJson "$ExportDocManagerDataRoot" "\" "/"

  ClearErrors
  FileOpen $0 "$ExportDocManagerRuntimePathsConfig" w
  IfErrors 0 runtime_paths_config_opened
    Abort "无法创建 $ExportDocManagerRuntimePathsConfig。"

  runtime_paths_config_opened:
  FileWrite $0 '{$\r$\n'
  FileWrite $0 '  "schemaVersion": 1,$\r$\n'
  FileWrite $0 '  "dataRoot": "$ExportDocManagerDataRootJson",$\r$\n'
  FileWrite $0 '  "source": "nsis-installer"$\r$\n'
  FileWrite $0 '}$\r$\n'
  FileClose $0

  runtime_paths_config_done:
!macroend

!macro NSIS_HOOK_PREINSTALL
  !insertmacro EXPORTDOCMANAGER_WARN_IF_SYSTEM_DRIVE_INSTALL
  !insertmacro EXPORTDOCMANAGER_CREATE_RUNTIME_DATA_ROOT
!macroend

!macro NSIS_HOOK_POSTINSTALL
  !insertmacro EXPORTDOCMANAGER_WRITE_RUNTIME_PATHS_CONFIG
!macroend

!macro NSIS_HOOK_PREUNINSTALL
  ${If} "$UpdateMode" != "1"
    Delete "$INSTDIR\runtime-paths.json"
  ${EndIf}
!macroend

!include FileFunc.nsh
!include LogicLib.nsh
!include StrFunc.nsh
${Using:StrFunc} StrRep

Var ExportDocManagerInstallDrive
Var ExportDocManagerSystemDrive
Var ExportDocManagerDataRoot
Var ExportDocManagerDataRootJson
Var ExportDocManagerRuntimePathsConfig

!macro EXPORTDOCMANAGER_ABORT_IF_SYSTEM_DRIVE_INSTALL
  ${GetRoot} "$WINDIR" $ExportDocManagerSystemDrive
  ${GetRoot} "$INSTDIR" $ExportDocManagerInstallDrive

  ${If} "$ExportDocManagerSystemDrive" != ""
  ${AndIf} "$ExportDocManagerInstallDrive" == "$ExportDocManagerSystemDrive"
    MessageBox MB_ICONEXCLAMATION|MB_OK "出口单证管理系统默认将程序依赖和业务数据放在非系统盘。请返回选择非系统盘安装目录，例如 D:\ExportDocManager。"
    Abort "出口单证管理系统安装目录必须位于 Windows 系统盘之外。"
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
    Abort "无法在 $ExportDocManagerDataRoot 创建出口单证管理系统运行数据目录。"

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
  !insertmacro EXPORTDOCMANAGER_ABORT_IF_SYSTEM_DRIVE_INSTALL
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

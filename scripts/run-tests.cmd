@echo off
setlocal
set "EXPORTDOCMANAGER_PS_SCRIPT=%~dp0run-tests.ps1"
"%~dp0lib\run-powershell-entry.cmd" %*

@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set CSC=%FW%\csc.exe
cd /d "%~dp0.."
"%CSC%" /nologo /target:exe /platform:anycpu /codepage:65001 /out:tools\LangFixerDriver.exe ^
  /r:System.dll /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  tools\Driver.cs Native.cs Layouts.cs Fixer.cs
if errorlevel 1 exit /b 1
echo Built tools\LangFixerDriver.exe

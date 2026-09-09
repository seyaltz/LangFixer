@echo off
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
cd /d "%~dp0.."
"%FW%\csc.exe" /nologo /target:exe /platform:anycpu /codepage:65001 /out:video\DemoTyper.exe ^
  /r:System.dll /r:"%FW%\WPF\UIAutomationClient.dll" /r:"%FW%\WPF\UIAutomationTypes.dll" /r:"%FW%\WPF\WindowsBase.dll" ^
  video\DemoTyper.cs Native.cs Layouts.cs Fixer.cs
if errorlevel 1 exit /b 1
echo Built video\DemoTyper.exe

@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /out:LangFixer.exe ^
  /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
  Native.cs Layouts.cs SpellCheck.cs CommonWords.cs Detector.cs DecisionService.cs Fixer.cs Injector.cs Engine.cs Hooks.cs Settings.cs DashboardForm.cs TrayApp.cs SelfTest.cs
if errorlevel 1 exit /b 1
echo Built LangFixer.exe

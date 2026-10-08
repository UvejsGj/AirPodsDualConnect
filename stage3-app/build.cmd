@echo off
rem Builds DualConnectTray.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem Installs nothing and needs no admin rights. Untested on your laptop.
rem Pass /nopause to skip the final "Press any key".
setlocal
cd /d "%~dp0"
set "RESULT=0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from the .NET Framework 4.x that ships with Windows.
  set "RESULT=1"
  goto done
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:DualConnectTray.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\TrayCore.cs src\DualConnectRunner.cs src\WinInterop.cs src\TrayApp.cs
if errorlevel 1 (
  echo Build failed. Copy the lines above into the project thread.
  set "RESULT=1"
  goto done
)
echo Built %CD%\DualConnectTray.exe
:done
if /i not "%~1"=="/nopause" pause
exit /b %RESULT%

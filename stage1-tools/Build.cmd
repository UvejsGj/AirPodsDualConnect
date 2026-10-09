@echo off
rem Builds DualConnect.exe (console) and DualConnectW.exe (no window) from DualConnect.cs
rem with the C# compiler that ships with .NET Framework 4 on Windows 10 and 11.
rem Creates two files in this folder. Installs nothing, needs no admin rights.
rem Add /nopause to skip the "Press any key" at the end.
setlocal
set "HERE=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe from .NET Framework 4 in %WINDIR%\Microsoft.NET.
  goto :failed
)
if not exist "%HERE%DualConnect.cs" (
  echo DualConnect.cs is not next to Build.cmd. Extract the whole folder from the zip first, then run Build.cmd from there.
  goto :failed
)
"%CSC%" /nologo /optimize+ /target:exe /platform:anycpu /out:"%HERE%DualConnect.exe" "%HERE%DualConnect.cs"
if errorlevel 1 goto :compilefailed
"%CSC%" /nologo /optimize+ /target:winexe /platform:anycpu /out:"%HERE%DualConnectW.exe" "%HERE%DualConnect.cs"
if errorlevel 1 goto :compilefailed
echo Built DualConnect.exe and DualConnectW.exe in "%HERE%"
if /i not "%~1"=="/nopause" pause
exit /b 0

:compilefailed
echo Build failed. If a message says the file is being used by another process, close the logger,
echo the tray app and any DualConnect window, then run Build.cmd again. Otherwise copy the
echo messages above into the project thread.
:failed
if /i not "%~1"=="/nopause" pause
exit /b 1

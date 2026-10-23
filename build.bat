@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
pushd "%~dp0src"
"%CSC%" /nologo /target:winexe /win32manifest:app.manifest /out:"%~dp0NteAutoFish.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll NteAutoFish.cs Engine.cs
if errorlevel 1 (echo BUILD FAILED & popd & exit /b 1)
popd
echo BUILD OK -^> NteAutoFish.exe
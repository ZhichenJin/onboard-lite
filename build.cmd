@echo off
rem Builds OnboardLite.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WPF=%FW%\WPF
"%FW%\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /warn:4 ^
  /out:OnboardLite.exe /win32icon:assets\app.ico /win32manifest:assets\app.manifest ^
  /r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:System.Xaml.dll ^
  /resource:ui\theme.xaml,OnboardLite.theme.xaml /resource:ui\shell.xaml,OnboardLite.shell.xaml ^
  /resource:ui\home.xaml,OnboardLite.home.xaml /resource:ui\device.xaml,OnboardLite.device.xaml ^
  src\*.cs
if errorlevel 1 exit /b 1
echo Built %CD%\OnboardLite.exe

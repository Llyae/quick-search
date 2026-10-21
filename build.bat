@echo off
rem Builds bin\QuickSearch.exe with the C# compiler that ships with Windows, then runs the tests.
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist bin mkdir bin

"%CSC%" /nologo /r:System.Web.Extensions.dll /out:bin\Tests.exe src\Config.cs tests\Tests.cs || exit /b 1
bin\Tests.exe || exit /b 1

"%CSC%" /nologo /r:System.Web.Extensions.dll /target:winexe /optimize /win32icon:assets\icon.ico /resource:assets\icon.ico,icon.ico /out:bin\QuickSearch.exe src\*.cs || exit /b 1
echo Built bin\QuickSearch.exe

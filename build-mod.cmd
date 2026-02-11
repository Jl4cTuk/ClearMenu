@echo off
setlocal

pushd "%~dp0"

set "CSPROJ="

for %%F in ("Source\*.csproj") do (
    set "CSPROJ=%%F"
    goto :found
)

echo No .csproj found in Source\
exit /b 1

:found
echo Building "%CSPROJ%"
dotnet build "%CSPROJ%" -c Release
if errorlevel 1 (
    echo Build failed.
    exit /b %errorlevel%
)

rmdir /s /q "Source\bin"
rmdir /s /q "Source\obj"

echo Build succeeded.

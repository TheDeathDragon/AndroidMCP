@echo off
setlocal
chcp 65001 >nul
cd /d "%~dp0"

set "RID=%~1"
if "%RID%"=="" set "RID=win-x64"

set "OUT=publish\%RID%"
if exist "%OUT%" rmdir /s /q "%OUT%"

dotnet publish src\AndroidMcp\AndroidMcp.csproj -c Release -r %RID% -o "%OUT%" --nologo
if errorlevel 1 (
    echo [ERROR] Publish failed.
    exit /b 1
)

if not exist "%OUT%\tools" mkdir "%OUT%\tools"
copy /y "tools\agent-server.jar" "%OUT%\tools\agent-server.jar" >nul

echo.
echo Published to: %CD%\%OUT%
dir /b "%OUT%"
endlocal

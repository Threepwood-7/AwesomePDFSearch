@echo off
setlocal

echo ============================================
echo  AwesomePDFSearch Build Script
echo ============================================
echo.

:: Check for MSBuild via vswhere (VS 2017+)
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [ERROR] vswhere not found. Visual Studio 2017 or later is required.
    echo         Download from https://visualstudio.microsoft.com/
    exit /b 1
)

for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
    set "MSBUILD=%%i"
)

if not defined MSBUILD (
    echo [ERROR] MSBuild not found. Install the ".NET desktop development" workload
    echo         in Visual Studio Installer.
    exit /b 1
)

echo [OK] MSBuild: %MSBUILD%

:: Check for .NET Framework 4.8.1 targeting pack
set "FXREF=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8.1"
if not exist "%FXREF%" (
    echo [WARN] .NET Framework 4.8.1 targeting pack not found.
    echo        Install it via Visual Studio Installer or from:
    echo        https://dotnet.microsoft.com/download/dotnet-framework/net481
    echo.
)

:: Restore NuGet packages
echo.
echo --- Restoring NuGet packages ---

where nuget >nul 2>&1
if %errorlevel% equ 0 (
    nuget restore AwesomePDFSearch.sln
) else (
    echo [INFO] nuget.exe not on PATH, attempting MSBuild restore...
    "%MSBUILD%" AwesomePDFSearch.sln /t:Restore /v:minimal
)

if %errorlevel% neq 0 (
    echo [ERROR] Package restore failed.
    exit /b 1
)

:: Build
echo.
echo --- Building (Release) ---
"%MSBUILD%" AwesomePDFSearch.sln /p:Configuration=Release /v:minimal

if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Build failed.
    exit /b 1
)

echo.
echo ============================================
echo  Build succeeded.
echo  Output: bin\Release\AwesomePDFSearch.exe
echo ============================================

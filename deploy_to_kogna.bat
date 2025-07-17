@echo off
REM Deploy Kogna UART Passthrough Program
REM This script compiles and deploys the TCC-compatible version

echo ========================================
echo Kogna UART Passthrough Deployment
echo ========================================

REM Check if TCC is available
echo Checking TCC availability...
tcc --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: TCC not found in PATH
    echo Please install TCC or add it to your PATH
    echo Download from: https://bellard.org/tcc/
    pause
    exit /b 1
)

echo TCC found. Compiling program...

REM Compile with TCC
tcc -Wall -O2 -o kogna_uart_passthrough.exe kogna_uart_passthrough_tcc.c -lKMotion

if errorlevel 1 (
    echo ERROR: Compilation failed
    echo Please check the source code and TCC installation
    pause
    exit /b 1
)

echo Compilation successful!

REM Check if executable was created
if not exist kogna_uart_passthrough.exe (
    echo ERROR: Executable not created
    pause
    exit /b 1
)

echo.
echo ========================================
echo Deployment Options:
echo ========================================
echo 1. Manual deployment (copy executable to Kogna)
echo 2. SCP deployment (requires SSH access)
echo 3. USB deployment (copy to USB drive)
echo.

set /p choice="Select deployment method (1-3): "

if "%choice%"=="1" goto manual
if "%choice%"=="2" goto scp
if "%choice%"=="3" goto usb
echo Invalid choice. Using manual deployment.
goto manual

:manual
echo.
echo ========================================
echo Manual Deployment
echo ========================================
echo Please copy the following file to your Kogna controller:
echo.
echo Source: %CD%\kogna_uart_passthrough.exe
echo.
echo Destination: [Kogna USB Drive]\C Programs\
echo.
echo Steps:
echo 1. Connect Kogna via USB
echo 2. Copy kogna_uart_passthrough.exe to the C Programs folder
echo 3. Flash the program using Dynomotion software
echo.
pause
goto end

:scp
echo.
echo ========================================
echo SCP Deployment
echo ========================================
set /p kogna_ip="Enter Kogna IP address: "
set /p kogna_user="Enter Kogna username: "
echo.
echo Copying executable to Kogna...
scp kogna_uart_passthrough.exe %kogna_user%@%kogna_ip%:/tmp/
if errorlevel 1 (
    echo ERROR: SCP transfer failed
    echo Please check network connection and credentials
    pause
    exit /b 1
)
echo.
echo Executable copied successfully!
echo Please flash the program using Dynomotion software
echo.
pause
goto end

:usb
echo.
echo ========================================
echo USB Deployment
echo ========================================
echo Please insert a USB drive and press any key...
pause >nul

REM Find USB drives
for /f "tokens=2 delims==" %%i in ('wmic logicaldisk where "drivetype=2" get deviceid /value ^| find "="') do (
    echo Found USB drive: %%i
    set usb_drive=%%i
)

if not defined usb_drive (
    echo ERROR: No USB drive found
    echo Please insert a USB drive and try again
    pause
    exit /b 1
)

echo Copying to USB drive: %usb_drive%
copy kogna_uart_passthrough.exe %usb_drive%\
if errorlevel 1 (
    echo ERROR: Failed to copy to USB drive
    pause
    exit /b 1
)

echo.
echo Executable copied to USB drive: %usb_drive%\kogna_uart_passthrough.exe
echo Please transfer this file to your Kogna controller
echo.
pause
goto end

:end
echo.
echo ========================================
echo Deployment Complete
echo ========================================
echo.
echo Next steps:
echo 1. Flash the program to Kogna using Dynomotion software
echo 2. Test the UART passthrough functionality
echo 3. Use the C# application to send commands
echo.
echo For testing, use commands like:
echo   rs485 1 3001
echo   rs232 48 65 6C 6C 6F
echo   uartconfig rs485 38400
echo.
pause 
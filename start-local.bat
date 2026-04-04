@echo off
chcp 65001 >nul
echo.
echo ========================================
echo   本地 WebSocket 伺服器
echo ========================================
echo.

REM 取得 Wi-Fi IP
for /f "tokens=2 delims=:" %%a in ('ipconfig ^| findstr /i "IPv4" ^| findstr /v "127.0.0.1"') do (
    set IP=%%a
    goto :found
)
:found
set IP=%IP: =%

echo 你的電腦 IP：%IP%
echo.
echo 手機請開瀏覽器連：
echo   http://%IP%:8080
echo.
echo Unity Server URL 請改為：
echo   ws://%IP%:8080
echo.
echo ========================================
echo.

cd /d "%~dp0"
node server.js

pause

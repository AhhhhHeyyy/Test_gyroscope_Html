@echo off
echo ========================================
echo SimpleWebRTC 系统测试启动脚本
echo ========================================
echo.

echo 正在启动信令服务器...
echo 请确保已安装 Node.js 和依赖项
echo.

cd /d "C:\Users\user\Desktop\School\Project1141"

echo 检查依赖项...
if not exist "node_modules" (
    echo 安装依赖项...
    npm install
)

echo.
echo 启动 WebSocket 信令服务器...
echo 服务器将在 http://localhost:8080 运行
echo WebRTC 测试页面: http://localhost:8080/simplewebrtc-sender.html
echo.

echo 按 Ctrl+C 停止服务器
echo.

node server.js

pause

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SimpleWebRTC 系统测试启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "正在启动信令服务器..." -ForegroundColor Yellow
Write-Host "请确保已安装 Node.js 和依赖项" -ForegroundColor Yellow
Write-Host ""

# 切换到项目目录
Set-Location "C:\Users\user\Desktop\School\Project1141"

Write-Host "检查依赖项..." -ForegroundColor Green
if (-not (Test-Path "node_modules")) {
    Write-Host "安装依赖项..." -ForegroundColor Yellow
    npm install
}

Write-Host ""
Write-Host "启动 WebSocket 信令服务器..." -ForegroundColor Green
Write-Host "服务器将在 http://localhost:8081 运行" -ForegroundColor Green
Write-Host "WebRTC 测试页面: http://localhost:8081/simplewebrtc-sender.html" -ForegroundColor Green
Write-Host ""

Write-Host "按 Ctrl+C 停止服务器" -ForegroundColor Red
Write-Host ""

# 启动服务器
node server.js

Write-Host ""
Write-Host "按任意键退出..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

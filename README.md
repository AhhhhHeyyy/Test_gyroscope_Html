# 陀螺儀WebSocket伺服器

這是一個支援WebSocket的陀螺儀數據傳輸伺服器，可以將手機陀螺儀數據即時傳送到Unity。

## 功能特色

- 📱 即時陀螺儀數據顯示
- 🔌 WebSocket實時數據傳輸
- 🎮 Unity整合支援
- 📊 視覺化圖表
- 🔒 HTTPS支援

## 快速開始

### 本地測試
```bash
npm install
npm start
```

### 部署
- Railway: 自動部署
- Render: 自動部署
- Vercel: 自動部署

## API端點

- `/` - 主頁
- `/gyroscope.html` - 陀螺儀測試頁面
- `/health` - 健康檢查
- `/api/status` - 服務狀態

## WebSocket連接

- 開發環境: `ws://localhost:3000`
- 生產環境: `wss://your-domain.com`

## Unity整合

使用提供的C#代碼連接WebSocket並接收陀螺儀數據。

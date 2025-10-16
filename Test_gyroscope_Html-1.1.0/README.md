# 📱 Unity WebSocket 陀螺儀數據傳輸專案

一個實現手機陀螺儀數據即時傳輸到Unity的WebSocket專案，支援跨平台實時數據同步。

## ✨ 功能特色

- 🔄 **即時數據傳輸**：手機陀螺儀數據即時傳送到Unity
- 🌐 **跨平台支援**：支援iOS、Android、桌面平台
- 🎮 **Unity整合**：完整的Unity C#腳本和組件
- ☁️ **雲端部署**：使用Railway進行雲端部署
- 📊 **即時監控**：完整的連接狀態和數據監控
- 🎨 **美觀界面**：現代化的網頁界面設計

## 🏗️ 專案架構

```
Project1141/
├── server.js                    # Node.js WebSocket伺服器
├── package.json                 # Node.js依賴配置
├── railway.toml                 # Railway部署配置
├── TestHtml/                    # 網頁端檔案
│   ├── index.html              # 主頁面（帶WebSocket功能）
│   ├── gyroscope.html          # 陀螺儀測試頁面
│   └── gyroscope-cube.html     # 3D立方體展示頁面
└── UnityWebsocket0927/         # Unity專案
    └── Assets/Scripts/         # Unity C#腳本
        ├── GyroscopeReceiver.cs    # WebSocket接收器
        ├── GyroscopeController.cs  # 陀螺儀控制器
        └── GyroscopeDebugger.cs    # 調試工具
```

## 🚀 快速開始

### 1. 環境要求

- **Node.js** >= 16.0.0
- **Unity** >= 2022.3 LTS
- **現代瀏覽器**（支援WebSocket和DeviceOrientation API）

### 2. 本地開發

#### 啟動伺服器
```bash
# 安裝依賴
npm install

# 啟動開發伺服器
npm start
# 或
node server.js
```

伺服器將在 `http://localhost:8080` 啟動

#### Unity設定
1. 打開Unity專案：`UnityWebsocket0927/`
2. 安裝NativeWebSocket套件：
   - Window → Package Manager
   - 搜索 "NativeWebSocket" 並安裝
3. 在場景中添加GyroscopeReceiver組件
4. 設定伺服器URL為：`wss://testgyroscopehtml-production.up.railway.app`

### 3. 雲端部署

#### Railway部署
```bash
# 安裝Railway CLI
npm install -g @railway/cli

# 登入並部署
railway login
railway up
```

#### 手動部署
1. 將專案推送到GitHub
2. 在Railway中連接GitHub倉庫
3. 自動部署完成

## 📱 使用說明

### 手機端操作
1. 在手機瀏覽器中訪問：`https://testgyroscopehtml-production.up.railway.app/`
2. 允許瀏覽器存取裝置方向感應器權限
3. 旋轉手機，觀察即時陀螺儀數據
4. 數據會自動傳送到Unity

### Unity端操作
1. 運行Unity場景
2. 觀察Console日誌確認連接狀態
3. 移動手機，Unity中的GameObject會相應旋轉

## 🔧 技術細節

### WebSocket協議
- **連接URL**：`wss://testgyroscopehtml-production.up.railway.app`
- **數據格式**：JSON
- **訊息類型**：
  - `connection`：連接確認
  - `gyroscope`：陀螺儀數據
  - `ack`：數據接收確認
  - `error`：錯誤訊息

### 數據結構
```json
{
  "type": "gyroscope",
  "data": {
    "alpha": 45.5,    // X軸旋轉 (0-360度)
    "beta": -12.3,    // Y軸傾斜 (-180到180度)
    "gamma": 78.9,    // Z軸傾斜 (-90到90度)
    "timestamp": 1759179307274,
    "clientId": 1
  }
}
```

### Unity組件說明

#### GyroscopeReceiver
- **功能**：WebSocket連接和數據接收
- **主要方法**：
  - `ConnectToServer()`：連接到伺服器
  - `GetLatestData()`：獲取最新數據
  - `Disconnect()`：斷開連接

#### GyroscopeController
- **功能**：將陀螺儀數據轉換為Unity物件旋轉
- **可調參數**：
  - 旋轉靈敏度
  - 平滑設定
  - 旋轉限制

#### GyroscopeDebugger
- **功能**：調試和監控工具
- **顯示資訊**：
  - 連接狀態
  - 數據數值
  - 佇列長度

## 📊 API端點

### 健康檢查
```
GET /health
```
返回伺服器基本狀態

### 詳細狀態
```
GET /api/status
```
返回詳細的伺服器狀態和統計資訊

### 保持活躍
```
GET /api/ping
```
用於檢查伺服器響應

## 🐛 故障排除

### 常見問題

#### 1. Unity無法連接
**症狀**：Unity Console顯示連接錯誤
**解決方案**：
- 檢查URL是否正確（使用 `wss://` 不是 `ws://`）
- 確認Railway伺服器正在運行
- 檢查防火牆設置

#### 2. 數據不更新
**症狀**：Unity連接成功但沒有收到數據
**解決方案**：
- 確認手機端正在發送數據
- 檢查Unity的消息解析邏輯
- 查看伺服器日誌

#### 3. 連接不穩定
**症狀**：頻繁斷線重連
**解決方案**：
- 檢查網絡穩定性
- 調整重連間隔
- 查看伺服器資源使用情況

### 調試工具

#### 1. 伺服器監控
```bash
# 檢查伺服器狀態
curl https://testgyroscopehtml-production.up.railway.app/api/status

# 查看Railway日誌
railway logs
```

#### 2. 瀏覽器調試
- 打開開發者工具
- 查看Console日誌
- 檢查WebSocket連接狀態

#### 3. Unity調試
- 查看Console面板
- 啟用GyroscopeDebugger的詳細日誌
- 檢查事件訂閱狀態

## 🔄 版本歷史

### v1.0.0
- 初始版本發布
- 基本WebSocket連接功能
- Unity陀螺儀控制
- Railway雲端部署

## 🤝 貢獻指南

1. Fork 專案
2. 創建功能分支 (`git checkout -b feature/AmazingFeature`)
3. 提交變更 (`git commit -m 'Add some AmazingFeature'`)
4. 推送到分支 (`git push origin feature/AmazingFeature`)
5. 開啟 Pull Request

## 📄 授權條款

本專案採用 MIT 授權條款 - 查看 [LICENSE](LICENSE) 檔案了解詳情

## 📞 支援與聯絡

- **問題回報**：[GitHub Issues](https://github.com/your-username/your-repo/issues)
- **功能建議**：[GitHub Discussions](https://github.com/your-username/your-repo/discussions)
- **技術文檔**：[TROUBLESHOOTING.md](TROUBLESHOOTING.md)

## 🙏 致謝

- [NativeWebSocket](https://github.com/endel/NativeWebSocket) - Unity WebSocket套件
- [Railway](https://railway.app/) - 雲端部署平台
- [Unity](https://unity.com/) - 遊戲引擎

---

**注意**：本專案僅供學習和開發使用，請確保在生產環境中進行適當的安全性和性能測試。

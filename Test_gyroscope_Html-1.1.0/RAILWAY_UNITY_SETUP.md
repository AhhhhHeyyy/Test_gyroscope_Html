# Railway + Unity WebSocket 即時數據傳輸設置指南

## 📋 概述
本指南將幫助您設置從Railway託管的網站到Unity的即時陀螺儀數據傳輸。

## 🚀 第一部分：Railway部署

### 1. 準備Railway部署
您的項目已經配置好Railway部署，包含以下文件：
- `server.js` - WebSocket服務器
- `package.json` - Node.js依賴
- `railway.toml` - Railway配置
- `TestHtml/` - 測試網頁

### 2. 部署到Railway
```bash
# 1. 安裝Railway CLI
npm install -g @railway/cli

# 2. 登入Railway
railway login

# 3. 初始化項目
railway init

# 4. 部署
railway up
```

### 3. 獲取Railway URL
部署完成後，Railway會提供一個URL，格式如：
```
https://your-app-name.railway.app
```

**重要**：WebSocket URL需要使用 `wss://` 協議：
```
wss://your-app-name.railway.app
```

## 🎮 第二部分：Unity設置

### 1. 安裝NativeWebSocket
在Unity中：
1. 打開 `Window > Package Manager`
2. 選擇 `Unity Registry`
3. 搜索 `NativeWebSocket`
4. 點擊 `Install`

### 2. 配置Unity腳本
更新 `GyroscopeReceiver.cs` 中的服務器URL：

```csharp
[SerializeField] private string serverUrl = "wss://your-app-name.railway.app";
```

### 3. Unity場景設置
1. 創建一個空的GameObject
2. 添加 `GyroscopeReceiver` 腳本
3. 添加 `GyroscopeController` 腳本
4. 在Inspector中設置：
   - **Server URL**: 您的Railway WebSocket URL
   - **Auto Connect**: 勾選
   - **Reconnect Interval**: 5秒

## 🔧 第三部分：測試連接

### 1. 測試WebSocket服務器
訪問您的Railway URL：
```
https://your-app-name.railway.app/health
```

應該看到類似：
```json
{
  "status": "ok",
  "uptime": 123,
  "connections": {
    "active": 0,
    "total": 0
  },
  "messages": 0,
  "timestamp": 1234567890
}
```

### 2. 測試WebSocket連接
1. 打開瀏覽器開發者工具
2. 在控制台執行：
```javascript
const ws = new WebSocket('wss://your-app-name.railway.app');
ws.onopen = () => console.log('連接成功');
ws.onmessage = (msg) => console.log('收到消息:', msg.data);
```

### 3. 測試Unity連接
1. 在Unity中運行場景
2. 查看Console日誌
3. 應該看到：
   - `🔌 WebSocket連接已建立`
   - `📱 收到訊息: {"type":"connection",...}`

## 📱 第四部分：完整測試流程

### 1. 啟動服務器
```bash
# 本地測試
npm start

# 或直接使用Railway部署的服務器
```

### 2. 測試網頁端
1. 訪問 `https://your-app-name.railway.app`
2. 允許瀏覽器訪問陀螺儀
3. 移動設備，觀察數據變化

### 3. 測試Unity端
1. 在Unity中運行場景
2. 觀察GameObject的旋轉
3. 檢查Console日誌

## 🐛 故障排除

### 常見問題

#### 1. Unity無法連接
**症狀**：Unity Console顯示連接錯誤
**解決方案**：
- 檢查URL是否正確（使用 `wss://` 不是 `ws://`）
- 確認Railway服務器正在運行
- 檢查防火牆設置

#### 2. 數據不更新
**症狀**：Unity連接成功但沒有收到數據
**解決方案**：
- 確認網頁端正在發送數據
- 檢查Unity的消息解析邏輯
- 查看服務器日誌

#### 3. 連接不穩定
**症狀**：頻繁斷線重連
**解決方案**：
- 檢查網絡穩定性
- 調整重連間隔
- 查看服務器資源使用情況

### 調試工具

#### 1. Railway日誌
```bash
railway logs
```

#### 2. Unity Console
查看Unity Console中的WebSocket日誌

#### 3. 瀏覽器開發者工具
檢查WebSocket連接狀態

## 📊 監控和維護

### 1. 服務器監控
訪問以下端點監控服務器狀態：
- `/health` - 基本健康檢查
- `/api/status` - 詳細狀態信息
- `/api/ping` - 保持活躍檢查

### 2. 性能優化
- 調整Unity的更新頻率
- 優化WebSocket消息大小
- 監控服務器資源使用

### 3. 安全考慮
- 考慮添加身份驗證
- 限制連接數量
- 實施速率限制

## 🎯 下一步

1. **自定義數據格式**：根據需要修改陀螺儀數據結構
2. **添加更多傳感器**：擴展支持加速度計、磁力計等
3. **優化性能**：實施數據過濾和插值
4. **添加UI**：創建連接狀態顯示界面

## 📞 支援

如果遇到問題：
1. 檢查Railway部署日誌
2. 查看Unity Console錯誤
3. 測試WebSocket連接
4. 確認所有URL和端口設置正確

---

**注意**：確保您的Railway URL是正確的，並且使用 `wss://` 協議進行安全連接。

# SimpleWebRTC 系統測試指南

## 系統架構

```
Web 瀏覽器 (simplewebrtc-sender.html)
    ↓ WebRTC 信令
Node.js 信令服務器 (server.js)
    ↓ WebRTC 信令  
Unity 客戶端 (SimpleWebRTCReceiver.cs)
```

## 測試步驟

### 1. 啟動信令服務器

```bash
cd C:\Users\user\Desktop\School\Project1141
node server.js
```

預期輸出:
```
🚀 陀螺儀 & 螢幕捕獲 WebSocket伺服器啟動成功!
📱 靜態檔案服務: http://localhost:8080
🔌 WebSocket端點: ws://localhost:8080
```

### 2. 配置 Unity 場景

1. 打開 Unity 項目
2. 創建新場景或使用現有場景
3. 添加空 GameObject，命名為 "WebRTCController"
4. 添加 `SimpleWebRTCReceiver` 組件
5. 配置以下字段:
   - WebSocket Server Address: `ws://localhost:8080`
   - Room ID: `default-room`
   - Video Display: 創建 RawImage 並拖拽到字段
   - Status Text: 創建 Text 並拖拽到字段

### 3. 啟動 Unity 客戶端

1. 運行 Unity 場景
2. 在 Inspector 中點擊 "Connect" 按鈕
3. 觀察狀態文本顯示 "WebSocket 已連接，等待 WebRTC 配對..."

### 4. 啟動 Web 發送端

1. 打開瀏覽器訪問: `http://localhost:8080/simplewebrtc-sender.html`
2. 點擊 "連接" 按鈕
3. 等待狀態顯示 "房間就緒，可以開始屏幕共享"
4. 點擊 "開始屏幕共享" 按鈕
5. 選擇要共享的屏幕/窗口

### 5. 驗證連接

預期結果:
- Unity 端狀態顯示 "WebRTC 連接已建立！"
- Unity 端 RawImage 顯示 Web 端的屏幕內容
- 控制檯輸出顯示視頻傳輸開始

## 故障排除

### 問題 1: Unity 端顯示灰屏

**可能原因:**
- WebRTC 連接未建立
- 視頻軌道未正確綁定
- 編碼器不兼容

**解決步驟:**
1. 檢查 Unity Console 日誌
2. 確認 Web 端屏幕共享已開始
3. 檢查 SimpleWebRTC 組件配置

### 問題 2: WebSocket 連接失敗

**可能原因:**
- 服務器未啟動
- 端口被佔用
- 防火牆阻止

**解決步驟:**
1. 確認 `node server.js` 正在運行
2. 檢查端口 8080 是否可用
3. 嘗試使用 `ws://127.0.0.1:8080`

### 問題 3: WebRTC 連接建立失敗

**可能原因:**
- ICE 候選交換失敗
- STUN 服務器不可達
- 網絡配置問題

**解決步驟:**
1. 檢查網絡連接
2. 嘗試不同的 STUN 服務器
3. 考慮使用 TURN 服務器

## 性能優化建議

### Unity 端
- 使用合適的視頻分辨率 (1280x720)
- 啟用硬件加速
- 定期清理未使用的資源

### Web 端
- 限制幀率到 30fps
- 使用 VP8 編碼器
- 優化 Canvas 繪製

### 服務器端
- 監控連接數量
- 定期清理無效連接
- 使用負載均衡 (多實例時)

## 部署到雲端

### Railway 部署
1. 更新 Unity 中的 WebSocket URL 為 Railway 地址
2. 確保使用 `wss://` 協議
3. 配置環境變量

### 注意事項
- 雲端部署需要 TURN 服務器
- 確保 HTTPS/WSS 證書有效
- 監控服務器資源使用

## 測試檢查清單

- [ ] 信令服務器啟動成功
- [ ] Unity 客戶端連接 WebSocket
- [ ] Web 端連接 WebSocket
- [ ] 房間配對成功
- [ ] WebRTC 連接建立
- [ ] 視頻流傳輸正常
- [ ] 無灰屏問題
- [ ] 控制檯無錯誤日誌

## 下一步

如果測試成功:
1. 集成到現有完整系統
2. 添加陀螺儀數據傳輸
3. 優化顯示比例 (1280x400)
4. 部署到生產環境

如果測試失敗:
1. 檢查日誌輸出
2. 逐步排查各組件
3. 參考故障排除指南
4. 考慮降級到 WebSocket 方案

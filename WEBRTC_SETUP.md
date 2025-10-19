# WebRTC 螢幕捕捉設置指南

## 📋 概述

本指南說明如何設置和使用 WebRTC 螢幕捕捉功能，包括 Railway 限制、故障排除和性能優化。

## 🏗️ 架構說明

### 傳輸模式

1. **WebRTC P2P（優先）**
   - 點對點直連，延遲最低（100-300ms）
   - 使用 STUN 伺服器進行 NAT 穿透
   - 自動適應網路條件和品質

2. **WebSocket 中繼（降級）**
   - 通過 Railway 伺服器中繼
   - 延遲較高（500-1000ms）
   - 確保 100% 可用性

### 連接流程

```
電腦瀏覽器 → Railway WebSocket（信令） → Unity
     ↓
WebRTC P2P 連接建立
     ↓
直接視頻流傳輸（不經過 Railway）
```

## ⚠️ Railway 限制

### 重要限制

- **僅支援 WebSocket（TCP）**：無法提供 UDP 服務
- **無法作為 TURN 伺服器**：不能中繼 WebRTC 媒體流
- **僅用於信令交換**：offer/answer/ice-candidate 消息

### 影響

- 跨網路連接成功率受限（30-50%）
- 需要至少一方有公網 IP 或友好的 NAT 類型
- 同網路內連接成功率較高（95%+）

## 🔧 設置步驟

### 1. Unity 設置

#### 安裝 WebRTC 套件
1. 打開 Unity Package Manager
2. 搜索 "WebRTC"
3. 安裝 `com.unity.webrtc` 套件（建議版本 3.0.0+）

#### 場景設置
1. 創建空 GameObject
2. 添加 `WebRTCScreenReceiver` 組件
3. 設置 `targetRenderer` 指向要顯示視頻的物件
4. 確保 `GyroscopeReceiver` 存在於場景中

### 2. 前端設置

#### 瀏覽器要求
- Chrome 80+（推薦）
- Firefox 75+
- Safari 13+（部分功能受限）
- Edge 80+

#### 權限設置
- 螢幕捕獲權限
- 自動允許（或手動允許）

### 3. 伺服器設置

#### Railway 部署
```bash
# 部署到 Railway
git add .
git commit -m "Add WebRTC signaling support"
git push origin main
```

#### 環境變數
無需額外環境變數，使用預設 STUN 伺服器。

## 📊 性能監控

### 前端監控指標

| 指標 | 說明 | 正常範圍 |
|------|------|----------|
| 位元率 | 視頻傳輸速率 | 500-2000 kbps |
| 幀率 | 視頻幀率 | 15-30 fps |
| RTT | 往返延遲 | 50-300 ms |
| 候選者類型 | 連接類型 | host/srflx/relay |

### Unity 監控

在 Scene 視圖中查看：
- ICE 連接狀態
- PeerConnection 狀態
- 視頻解析度

## 🐛 故障排除

### 常見問題

#### 1. 黑畫面問題
**症狀**：Unity 收到信令但沒有視頻顯示

**可能原因**：
- WebRTC P2P 連接失敗
- 視頻軌道未正確接收
- VideoRenderer 未正確設置

**解決方案**：
1. 檢查 Console 日誌中的 ICE 狀態
2. 確認候選者類型（應為 host 或 srflx）
3. 檢查 Unity 中的 WebRTC 狀態顯示

#### 2. 只同網路可用
**症狀**：同一網路內正常，跨網路失敗

**原因**：缺少 TURN 伺服器

**解決方案**：
- 使用外部 TURN 服務（如 coturn）
- 或接受 WebSocket 降級模式

#### 3. 18秒超時降級
**症狀**：WebRTC 連接超時，自動切換到 WebSocket

**原因**：NAT 類型不友好或網路限制

**解決方案**：
- 檢查防火牆設置
- 嘗試不同的網路環境
- 使用 WebSocket 模式（功能正常）

#### 4. 信令連接失敗
**症狀**：無法建立 WebRTC 連接

**檢查項目**：
1. Railway 伺服器是否運行
2. WebSocket 連接是否正常
3. 房間 ID 是否匹配
4. 角色註冊是否成功

### 調試步驟

#### 1. 檢查信令流程
```javascript
// 在瀏覽器 Console 中
console.log('WebRTC 支援:', !!window.RTCPeerConnection);
console.log('WebSocket 狀態:', gyroWS.ws.readyState);
```

#### 2. 檢查 ICE 候選者
```javascript
// 查看候選者類型
peerConnection.onicecandidate = (event) => {
    if (event.candidate) {
        console.log('ICE 候選者:', event.candidate.candidate);
    }
};
```

#### 3. Unity 調試
- 啟用 `showDebugInfo` 選項
- 查看 Console 中的 WebRTC 狀態
- 檢查 `WebRTCScreenReceiver` 組件狀態

## 🚀 性能優化

### 視頻品質調整

#### 前端設置
```javascript
// 調整視頻參數
const constraints = {
    video: {
        width: { ideal: 1280, max: 1920 },
        height: { ideal: 720, max: 1080 },
        frameRate: { ideal: 30, max: 60 }
    }
};

// 設置 contentHint
videoTrack.contentHint = 'text'; // 文字清晰
// 或
videoTrack.contentHint = 'motion'; // 動態內容
```

#### Unity 設置
- 使用 `VideoRenderer` 而非直接操作 Texture
- 避免每幀重建材質
- 設置適當的 RenderTexture 尺寸

### 網路優化

#### STUN 伺服器配置
```javascript
const rtcConfig = {
    iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { urls: 'stun:stun1.l.google.com:19302' },
        { urls: 'stun:stun2.l.google.com:19302' }
    ],
    iceCandidatePoolSize: 10
};
```

#### 可選：TURN 伺服器
```javascript
// 使用外部 TURN 服務
const rtcConfig = {
    iceServers: [
        { urls: 'stun:stun.l.google.com:19302' },
        { 
            urls: 'turn:your-turn-server.com:3478',
            username: 'your-username',
            credential: 'your-password'
        }
    ]
};
```

## 📈 成功率預期

### 連接成功率

| 網路環境 | WebRTC 成功率 | 說明 |
|----------|---------------|------|
| 同一網路 | 95%+ | 使用 host candidates |
| 一方公網 IP | 70-80% | 使用 srflx candidates |
| 雙 NAT | 30-50% | 需要 TURN 伺服器 |
| 企業防火牆 | 10-30% | 可能完全阻擋 |

### 降級策略

- **10秒超時**：嘗試 `restartIce()`
- **18秒超時**：降級到 WebSocket
- **自動重試**：WebSocket 模式確保功能可用

## 🔮 未來改進

### 短期改進
- 手動模式切換按鈕
- 更詳細的狀態監控
- 連接品質指示器

### 長期改進
- 整合 TURN 伺服器（提升跨 NAT 成功率）
- 視頻品質自適應算法
- 多房間支援
- 安全 token 驗證

## 📚 參考資源

- [WebRTC 官方文檔](https://webrtc.org/)
- [Unity WebRTC 套件](https://docs.unity3d.com/Packages/com.unity.webrtc@latest/)
- [STUN/TURN 伺服器設置](https://github.com/coturn/coturn)
- [Railway 平台限制](https://docs.railway.app/)

## 🆘 支援

如遇到問題，請檢查：
1. 瀏覽器 Console 日誌
2. Unity Console 日誌
3. Railway 伺服器狀態
4. 網路連接狀況

常見問題請參考上述故障排除章節。

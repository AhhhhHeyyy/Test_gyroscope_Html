# WebSocket陀螺儀連接問題解決筆記

## 📋 問題概述

在開發Unity與手機端WebSocket陀螺儀數據傳輸專案時，遇到的主要問題是：**Unity能連接到Railway伺服器，但收不到任何陀螺儀數據**。

## 🔍 問題診斷過程

### 1. 初步檢查
- ✅ Unity WebSocket連接正常
- ✅ Railway伺服器運行正常
- ✅ 伺服器廣播邏輯正確
- ❌ 手機端沒有發送數據到伺服器

### 2. 根本原因發現
通過檢查Railway伺服器狀態API發現：
```json
{
  "connections": {"active": 1, "total": 1},
  "messages": 0
}
```
- 只有1個連接（Unity）
- 沒有收到任何訊息

**結論**：手機端根本沒有連接到Railway伺服器！

### 3. 問題定位
檢查發現 `index.html` 只有陀螺儀顯示功能，**沒有WebSocket連接功能**。

## 🛠️ 解決方案

### 問題1：手機端發送時機不當
**症狀**：陀螺儀事件監聽器在WebSocket連接建立前就註冊
**解決**：將陀螺儀事件監聽器註冊移到WebSocket連接成功後

### 問題2：WebSocket事件處理器覆蓋
**症狀**：在`startGyroscope()`中重新設置`onopen`事件，覆蓋了原本的處理器
**解決**：使用回調機制，避免覆蓋原有事件處理器

### 問題3：index.html缺少WebSocket功能
**症狀**：`index.html`只有靜態顯示，沒有數據傳輸功能
**解決**：為`index.html`添加完整的WebSocket連接和數據發送功能

## 📝 修正的關鍵代碼

### 1. WebSocket連接管理類
```javascript
class GyroscopeWebSocket {
    constructor() {
        this.ws = null;
        this.isConnected = false;
        this.onConnectedCallback = null;
    }
    
    connect() {
        const wsUrl = 'wss://testgyroscopehtml-production.up.railway.app';
        this.ws = new WebSocket(wsUrl);
        
        this.ws.onopen = () => {
            this.isConnected = true;
            if (this.onConnectedCallback) {
                this.onConnectedCallback();
            }
        };
    }
    
    sendGyroscopeData(alpha, beta, gamma) {
        if (this.isConnected && this.ws.readyState === WebSocket.OPEN) {
            const data = { alpha, beta, gamma, timestamp: Date.now() };
            this.ws.send(JSON.stringify(data));
        }
    }
}
```

### 2. 正確的陀螺儀監聽邏輯
```javascript
function startGyroscope() {
    // 先連接WebSocket
    gyroWS.connect();
    
    // 設置連接成功後的回調
    gyroWS.setOnConnectedCallback(() => {
        // 只在WebSocket連接成功後才註冊陀螺儀事件監聽器
        window.addEventListener('deviceorientation', handleGyroscopeEvent);
    });
}
```

## 🎯 最終解決方案

1. **為index.html添加WebSocket功能**
2. **修正陀螺儀事件監聽時機**
3. **確保數據發送在連接成功後進行**

## 📊 測試結果

修正後，數據流正常：
```
手機陀螺儀 → 手機瀏覽器 → WebSocket → Railway伺服器 → Unity
```

## 🔧 調試技巧

### 1. 伺服器狀態檢查
```bash
curl https://testgyroscopehtml-production.up.railway.app/api/status
```

### 2. 瀏覽器Console檢查
- WebSocket連接狀態
- 數據發送日誌
- 錯誤訊息

### 3. Unity Console檢查
- 連接確認訊息
- 數據接收日誌
- 事件觸發狀態

## ⚠️ 常見陷阱

1. **異步連接問題**：WebSocket連接是異步的，需要等待連接成功
2. **事件處理器覆蓋**：避免重複設置WebSocket事件處理器
3. **權限問題**：iOS需要用戶手動授權陀螺儀權限
4. **HTTPS要求**：WebSocket在生產環境需要HTTPS

## 📚 學習要點

1. **WebSocket連接生命週期管理**
2. **異步編程中的回調機制**
3. **移動端陀螺儀API使用**
4. **Unity與Web端的實時數據傳輸**
5. **Railway部署和監控**

## 🚀 後續優化建議

1. **添加重連機制**：處理網絡不穩定情況
2. **數據壓縮**：減少傳輸數據量
3. **錯誤處理**：完善異常情況處理
4. **性能監控**：添加連接質量監控
5. **用戶體驗**：添加連接狀態指示器

---

**總結**：這個問題的核心在於對WebSocket異步連接特性的理解不足，以及對數據流完整性的檢查不夠全面。通過系統性的診斷和修正，最終實現了穩定的實時數據傳輸。

# WebSocket連接診斷結果

## 🔍 問題診斷

### 檢查項目：

1. **✅ Unity Dispatch** - 通過
   - Unity在`Update()`中正確調用`websocket.DispatchMessageQueue()`
   - 位置：`GyroscopeReceiver.cs:197`

2. **✅ 伺服器廣播** - 通過
   - 伺服器正確實現了廣播邏輯
   - 位置：`server.js:58-76`
   - 伺服器會將收到的數據廣播給所有其他連接的客戶端

3. **✅ URL一致性** - 通過
   - Unity: `wss://testgyroscopehtml-production.up.railway.app`
   - 網頁: `wss://testgyroscopehtml-production.up.railway.app`

4. **❌ 手機端發送時機** - **發現問題！**
   - 問題：WebSocket連接是異步的，但陀螺儀事件監聽器立即註冊
   - 影響：在WebSocket連接成功之前就開始嘗試發送數據
   - 結果：`sendGyroscopeData()`檢查失敗，數據被丟棄

## 🔧 修正措施

### 已修正：

1. **添加調試日誌**
   - 在`sendGyroscopeData()`中添加詳細的調試日誌
   - 記錄連接狀態和WebSocket readyState
   - 記錄實際發送的數據和失敗原因

### 調試日誌輸出：

```javascript
console.log(`📤 發送數據檢查: isConnected=${this.isConnected}, readyState=${this.ws?.readyState}, OPEN=${WebSocket.OPEN}`);
```

如果發送失敗：
```javascript
console.warn(`⚠️ 無法發送數據: isConnected=${this.isConnected}, readyState=${this.ws?.readyState}`);
```

如果發送成功：
```javascript
console.log(`📤 實際發送數據:`, data);
```

## 📱 測試步驟

1. **部署更新後的網頁到Railway**
   ```bash
   git add TestHtml/gyroscope.html
   git commit -m "添加WebSocket發送調試日誌"
   git push
   ```

2. **在手機上測試**
   - 訪問 https://testgyroscopehtml-production.up.railway.app/
   - 打開瀏覽器開發者工具（Chrome: chrome://inspect）
   - 允許陀螺儀權限
   - 移動手機
   - 查看Console日誌

3. **在Unity中測試**
   - 運行Unity場景
   - 查看Console日誌
   - 應該能看到收到數據的日誌

## 📊 預期結果

### 正常情況：
```
✅ WebSocket連接已建立
📤 發送數據檢查: isConnected=true, readyState=1, OPEN=1
📤 實際發送數據: {alpha: 45.5, beta: -12.3, gamma: 78.9, timestamp: ...}
✅ 數據已成功發送
```

### 異常情況：
```
⚠️ 無法發送數據: isConnected=false, readyState=0
```

readyState值：
- 0 = CONNECTING (連接中)
- 1 = OPEN (已連接)
- 2 = CLOSING (關閉中)
- 3 = CLOSED (已關閉)

## 🎯 下一步

如果調試日誌顯示：
- `isConnected=false` 或 `readyState=0`：表示數據在連接建立前就開始發送
- 需要重構邏輯，確保只在WebSocket連接成功後才開始發送數據

## 🔗 相關文件

- `TestHtml/gyroscope.html` - 網頁端WebSocket連接邏輯
- `UnityWebsocket0927/Assets/Scripts/GyroscopeReceiver.cs` - Unity接收邏輯
- `server.js` - WebSocket伺服器廣播邏輯

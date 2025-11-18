## 螢幕捕獲使用指南（Web ↔ Unity，本機與基礎原理）

本指南手把手帶你：
- 啟動正確的伺服器與頁面
- 在前端開始螢幕/攝影機分享
- 在 Unity 端接收 WebRTC 影音並貼到材質
- 了解信令/SDP/ICE 的基本原理與常見問題


---


### 重要!!!! 檔案與掛載對照表（你需要用到的東西在哪、要掛哪裡）

前端與伺服器：
- `webrtc-server.js`
  - 位置：專案根目錄 `C:\Users\user\Desktop\School\Project1141\webrtc-server.js`
  - 用途：提供靜態檔案（含 `Screen1020.html`）與 WebRTC 信令（ws://localhost:8081）
  - 啟動：PowerShell 執行 `node .\webrtc-server.js`

- `TestHtml/Screen1020.html`
  - 位置：`C:\Users\user\Desktop\School\Project1141\TestHtml\Screen1020.html`
  - 用途：前端發送端（分享螢幕/攝影機），自動建立 Offer，並透過信令伺服器交換 ICE
  - 存取：瀏覽器開 `http://localhost:8081/Screen1020.html`

Unity 端（接收端）：
- `UnityWebsocket0927/Assets/Scripts/TestScreenCon.cs`
  - 位置：`UnityWebsocket0927/Assets/Scripts/TestScreenCon.cs`
  - 用途：Unity WebRTC 接收端腳本，處理 Offer/Answer/Candidate 與 `OnTrack` 視訊貼圖
  - 場景掛載：
    1) 在場景中選擇或建立一個 `GameObject`（可命名 `WebRTCReceiver`）
    2) 在該 `GameObject` 上新增組件並指派腳本 `TestScreenCon`
    3) `targetRenderer` 欄位：拖入要顯示畫面的 `Renderer`（例如一個 `Quad` 或 `Plane` 的 `MeshRenderer`）
    4) 參數：
       - `signalingUrl` 設為 `ws://localhost:8081`
       - `roomId` 設為 `default-room`（或與前端一致的自訂房間）

- 顯示用物件（材質貼圖）
  - 位置：在當前場景（例如 `Main.unity` 或 `ScreenTest.unity`）中建立的 `Quad/Plane/UI RawImage`
  - 用途：承接 `TestScreenCon` 於 `OnTrack` 收到的視訊紋理，顯示遠端畫面
  - 掛載點：
    - 若是 3D 物件（`Quad/Plane`）：將其 `MeshRenderer` 指派給 `TestScreenCon.targetRenderer`
    - 若想用 UI 顯示：可改為在 `OnTrack` 回調中把 `Texture` 指給 `RawImage.texture`（需自定義簡單腳本）

（可選）非 WebRTC 圖片幀顯示方案：
- `UnityWebsocket0927/Assets/Scripts/GyroscopeReceiver.cs`
  - 位置：`UnityWebsocket0927/Assets/Scripts/GyroscopeReceiver.cs`
  - 用途：透過 WebSocket 接收資料（含 `screen_capture` 類型的影像陣列）

- `UnityWebsocket0927/Assets/Scripts/ScreenCaptureHandler.cs`
  - 位置：`UnityWebsocket0927/Assets/Scripts/ScreenCaptureHandler.cs`
  - 用途：接收 `GyroscopeReceiver.OnScreenCaptureReceived` 事件，解碼圖片並貼到材質
  - 場景掛載：
    1) 在場景中選擇一個顯示用 `GameObject`（同樣可用 `Quad/Plane`）
    2) 掛上 `ScreenCaptureHandler` 腳本
    3) `targetRenderer` 指派到顯示物件的 `Renderer`
    4) 運行時會訂閱 `GyroscopeReceiver` 的事件並更新貼圖

快速核對：
- 啟動用：`webrtc-server.js`（必須先跑）
- 前端頁：`TestHtml/Screen1020.html`（在 8081 上打開）
- Unity 腳本：`TestScreenCon.cs`（掛到任一物件），`targetRenderer` 指向要顯示畫面的 `Renderer`
- 房間：前後端 `roomId` 必須一致（預設 `default-room`）




### 一、環境需求
- **Node.js** ≥ 16
- Windows PowerShell 或 CMD（注意不同殼層的指令用法）
 - Unity 建議 2022.3 LTS（若要使用 `TestScreenCon.cs` WebRTC 接收）

---

### 二、安裝依賴（若尚未安裝過）
在專案根目錄執行：

```powershell
cd C:\Users\user\Desktop\School\Project1141
npm install
```

---

### 三、啟動信令與靜態檔案伺服器（供 Screen1020.html 使用）
`Screen1020.html` 內使用的信令位址為 `ws://localhost:8081`，請啟動 `webrtc-server.js`。

- PowerShell：
```powershell
cd C:\Users\user\Desktop\School\Project1141
node .\webrtc-server.js
```

- CMD：
```bat
cd /d C:\Users\user\Desktop\School\Project1141
node webrtc-server.js
```

啟動成功後你會在主控台看到：
- 靜態檔案服務: `http://localhost:8081`
- WebSocket 端點: `ws://localhost:8081`
- 健康檢查: `http://localhost:8081/health`
- 狀態監控: `http://localhost:8081/api/status`

> 提醒：PowerShell 不支援 `&&` 作為指令分隔，請用分號 `;` 或分行執行。

---

### 四、開啟發送端頁面（選螢幕或攝影機）
瀏覽器開啟：

```
http://localhost:8081/Screen1020.html
```

頁面提供：
- 「分享螢幕」：呼叫 `navigator.mediaDevices.getDisplayMedia({ video: true })`
- 「啟用攝影機」：呼叫 `navigator.mediaDevices.getUserMedia({ video: true })`

點擊按鈕後，瀏覽器會要求權限授權；成功後頁面會：
1. 取得本地媒體流並預覽
2. 立即建立 RTCPeerConnection 並 `createOffer()`
3. 透過 WebSocket 將 SDP/ICE 傳到信令伺服器（房間預設 `default-room`）

小技巧：
- 如果改用不同房間，可在 `Screen1020.html` 中調整 `ROOM_ID`，Unity 端也要一致。
- 本頁面同時提供「攝影機」測試，便於快速檢查 WebRTC 管道是否正常。

---

### 五、Unity 接收端（詳細步驟）
使用 `UnityWebsocket0927/Assets/Scripts/TestScreenCon.cs`：

1) 在場景中新增一個可見物件（例如 Quad/Plane），確保掛有 `Renderer`。
2) 將 `TestScreenCon` 掛到任一 GameObject，並設定：
   - `signalingUrl = ws://localhost:8081`
   - `roomId = default-room`（或你的自訂房間）
   - `targetRenderer =` 你的渲染器（將把收到的遠端視訊貼成材質）
3) 執行 Unity 場景後，Console 會顯示加入房間、等待 Offer、收到視訊流等日誌。
4) 前端在 `Screen1020.html` 點擊「分享螢幕」或「啟用攝影機」後，應能在 Unity 中看到畫面貼圖更新。

注意事項：
- 若使用 URP/HDRP，腳本已同時嘗試設定 `_BaseMap` 與 `_MainTex`，仍看不到畫面時，請檢查材質着色器屬性是否匹配。
- 若出現黑畫面但日誌正常，嘗試更換材質/着色器或查看 Console 是否有 GPU/紋理格式提示。

---

### 六、原理速讀（信令/SDP/ICE/房間/角色）

角色與房間：
- 前端作為 `web-sender`，Unity 作為 `unity-receiver`，雙方以 `{ type: 'join', room, role }` 加入相同 `roomId`。
- 當房間內有兩端就緒時，伺服器會廣播 `ready`，雙方可以開始 WebRTC 協商。

信令流程（簡化）：
1. 前端取得媒體流後 `createOffer()`，透過 WebSocket 發送 `offer` 至房間。
2. Unity 收到 `offer`，`SetRemoteDescription(Offer)` → `CreateAnswer()` → `SetLocalDescription(Answer)`，再把 `answer` 回傳。
3. 雙方持續交換 `candidate`（ICE 候選），嘗試打洞建立 P2P 連線。

網路要點：
- STUN：本專案使用 `stun:stun.l.google.com:19302` 用於發現公網位址。
- 本機同網測試通常可行；跨 NAT/防火牆場合可能需要自行配置 TURN 伺服器。

訊息小抄（信令服務器 `webrtc-server.js`）：
- `join`、`ready`、`offer`、`answer`、`candidate` 會在同房間內轉發。
- 非信令類資料（例如測試的 `ready` ping）不會轉發給另一端。

---

### 七、常見問題與排查
- **PowerShell 指令錯誤（'&&' 不是有效分隔）**：分行執行或使用分號 `;`，或改用 CMD。
- **頁面顯示 WebSocket 連線錯誤**：
  - 確認伺服器已啟動且為 `8081`。
  - 防火牆允許 Node.js 連線。
- **無法彈出分享視窗/黑畫面**：
  - `getDisplayMedia` 需安全環境（localhost 屬允許範圍）。
  - 使用 Chromium/Edge/Firefox 最新版。
- **無法連線/ICE 失敗**：
  - 目前使用 Google STUN：`stun:stun.l.google.com:19302`，請確保網路可達。
  - 同機本地測試通常可行；跨網多 NAT 可能需要 TURN。
 - **Unity 收到 Offer 但無畫面**：確認 `targetRenderer` 綁定、材質屬性鍵、Console 是否顯示收到遠端視訊的回呼。
 - **房間不就緒**：請確認兩端都已 `join` 同一 `roomId`，伺服器日誌會輸出 `ready`。

---

### 八、驗證與診斷
- 健康檢查：`http://localhost:8081/health`
- 詳細狀態：`http://localhost:8081/api/status`
- 瀏覽器開發者工具 Console：觀察 `Screen1020.html` 的日誌（連線、offer/answer、candidate）
 - 伺服器主控台：可見 `offer/answer/candidate` 的轉發記錄與房間就緒提示
 - Unity Console：應顯示「收到远端视频流」，並在材质上看到畫面

---

### 九、快速流程（TL;DR）
1. 打開終端：
   - PowerShell：
   ```powershell
   cd C:\Users\user\Desktop\School\Project1141
   node .\webrtc-server.js
   ```
2. 瀏覽器開啟 `http://localhost:8081/Screen1020.html`
3. 點「分享螢幕」或「啟用攝影機」，允許權限
4. 在 Unity 端使用 `TestScreenCon.cs` 加入同一房間，完成協商後即可看到影像

---

### 十、延伸閱讀
- `README.md`：總覽與快速開始（含兩種伺服器與端口說明）
- `WEBRTC_SETUP.md`：更完整的 WebRTC 設定/疑難排解
- `TestHtml/Screen1020.html`：前端來源碼（按鈕、Offer、Candidate 交換）
- `UnityWebsocket0927/Assets/Scripts/TestScreenCon.cs`：Unity WebRTC 接收端

---

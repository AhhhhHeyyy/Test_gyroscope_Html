# 手機感測器 × WebSocket × Unity 互動系統

> 用手機的陀螺儀與加速度儀，透過 WebSocket 即時控制 Unity 場景中的物件。

---

## 1. 系統概述（Overview）

### 白話說明

這套系統讓你可以**拿著手機當控制器**：手機傾斜時，Unity 場景裡的球就跟著移動；旋轉手機上的虛擬旋鈕，Unity 也能收到旋轉角度。就像把手機變成一個無線遊戲手把。

### 技術總結

```
手機瀏覽器（感測器 + 旋鈕 UI）
    ↕ WebSocket（JSON 訊息）
Node.js 伺服器（廣播轉發）
    ↕ WebSocket（NativeWebSocket）
Unity 場景（接收資料 → 控制物件）
```

### 解決什麼問題？

- 手機瀏覽器的感測器 API（`DeviceMotion`、`DeviceOrientation`）**無法直接和 Unity 通訊**
- 這套系統在中間架設 WebSocket 中繼伺服器，讓雙方可以即時互傳資料
- 不需要安裝 App，只要開瀏覽器就能控制 Unity

### 適用情境

| 情境 | 說明 |
|------|------|
| Unity 互動裝置 | 展覽、互動投影、遊戲原型 |
| 手機感測控制 | 用傾斜控制角色移動、視角轉換 |
| 旋鈕虛擬介面 | 手機當旋轉控制器，Unity 接收角度 |
| AR 相機追蹤 | 將 AR 頁面偵測到的相機位姿傳入 Unity |

---

## 2. 系統架構（Architecture）

### 整體資料流

```
┌─────────────────────────────────────────────────────┐
│                    手機瀏覽器                         │
│                                                     │
│  SpinTest/index.html + ani.js                       │
│  ┌──────────────────────────────────────────────┐   │
│  │  旋鈕 UI（GSAP Draggable）                    │   │
│  │  ・拖拽旋鈕 → 計算角度                        │   │
│  │  ・超過 45° 觸發 snap（吸附）                 │   │
│  │  ・播放音效 + 震動                            │   │
│  └──────────────────────────────────────────────┘   │
│                                                     │
│  [未來] gyroscope.html（陀螺儀 / 加速度儀發送頁面）    │
└────────────────────┬────────────────────────────────┘
                     │ WebSocket JSON 訊息
                     ↓
┌─────────────────────────────────────────────────────┐
│              server.js（Node.js + Express）          │
│                                                     │
│  ・接收所有客戶端的訊息                              │
│  ・依 type 辨識（gyroscope / spin / shake / ...）    │
│  ・廣播給其他所有已連線的客戶端（包含 Unity）         │
└────────────────────┬────────────────────────────────┘
                     │ WebSocket JSON 訊息
                     ↓
┌─────────────────────────────────────────────────────┐
│                    Unity 場景                        │
│                                                     │
│  GyroscopeReceiver.cs                               │
│  ・連線到 WebSocket 伺服器                           │
│  ・解析訊息 type → 觸發對應的 C# 事件               │
│         │                                           │
│         ├─ OnAccelerationReceived                   │
│         │       ↓                                   │
│         │  AccelerometerBallEffect.cs               │
│         │  ・接收加速度向量                          │
│         │  ・低通濾波 + SmoothDamp                  │
│         │  ・移動球體物件位置                        │
│         │                                           │
│         ├─ OnSpinDataReceived（旋轉事件）            │
│         ├─ OnShakeDataReceived（搖晃事件）           │
│         ├─ OnGyroscopeDataReceived（陀螺儀角度）     │
│         └─ OnARCameraPoseReceived（AR 相機位姿）     │
└─────────────────────────────────────────────────────┘
```

### 各腳本職責一覽

| 檔案 | 位置 | 職責 |
|------|------|------|
| `index.html` | SpinTest/ | 旋鈕 UI 頁面（靜態 HTML 殼） |
| `ani.js` | SpinTest/ | 旋鈕拖拽邏輯、音效、震動 |
| `server.js` | 根目錄 | WebSocket 中繼伺服器（廣播） |
| `GyroscopeReceiver.cs` | Unity/Scripts | WebSocket 接收 + 事件分派 |
| `AccelerometerBallEffect.cs` | Unity/Scripts | 根據加速度移動球體 |

---

## 3. 腳本說明（Scripts Breakdown）

---

### `index.html` + `ani.js`（旋鈕互動頁面）

#### 功能說明
在手機瀏覽器中顯示一個可旋轉的旋鈕圖片。使用者用手指拖轉旋鈕，超過閾值就會**吸附（snap）** 到固定角度（90°），並同時觸發音效與手機震動。

#### 核心參數

| 變數 | 用途 | 預設值 |
|------|------|--------|
| `ROTATE_THRESHOLD` | 每旋轉幾度播放一次滾動音效 | `15`（度） |
| 拖拽限制閾值 | 旋轉增量超過 45° 時觸發吸附 | 45°（硬編碼） |
| 吸附目標 | 每次吸附跳 90° | ±90° |
| `duration` | 回彈動畫時間 | 0.4s / 0.6s |
| 震動時長 | 觸發 `navigator.vibrate` | 200ms |

#### 核心邏輯（步驟）

```
1. 頁面載入 → 使用 GSAP Draggable 建立旋轉拖拽元件
2. 使用者開始拖拽 → 記錄起始角度 startAngle
3. 拖拽過程中：
   a. 每旋轉 15° → 播放滾動音效（cada3.MP3）
   b. 若增量 > 45° → 強制吸附到 startAngle ± 90°
                    → 播放回彈音效 + 震動 200ms
4. 放開手指（DragEnd）：
   a. 增量 > 45° → 吸附到 ±90°，播放音效 + 震動
   b. 增量 ≤ 45° → 回彈到起始角度
5. 以 elastic.out 緩動動畫產生彈性回彈效果
```

#### 何時觸發
- 使用者手指在手機螢幕上拖拽旋鈕圖片時

---

### `server.js`（WebSocket 中繼伺服器）

#### 功能說明
這是整套系統的**訊息中樞**。它是一個 Node.js 伺服器，同時提供靜態網頁服務（TestHtml 資料夾）和 WebSocket 廣播功能。任何一個客戶端（手機/Unity）送來訊息，伺服器就轉發給**所有其他**已連線的客戶端。

#### 核心參數

| 設定 | 說明 | 值 |
|------|------|-----|
| 監聽埠 | HTTP + WebSocket | `8080`（或 `PORT` 環境變數） |
| 靜態資料夾 | 服務 HTML 檔案 | `./TestHtml/` |
| 心跳清理 | 定期移除斷線客戶端 | 每 30 秒 |
| 狀態報告 | Console 輸出連線狀況 | 每 60 秒 |

#### 支援的訊息類型

| `type` 欄位 | 說明 |
|-------------|------|
| `join` | 客戶端加入房間（不廣播，只回應） |
| `claim` | 手機宣告控制權（不廣播，只回應） |
| `gyroscope` | 陀螺儀角度（alpha/beta/gamma） |
| `shake` | 搖晃事件（強度、次數） |
| `spin` | 旋鈕旋轉事件（角度） |
| `spin_mode` | 旋鈕模式切換（90° / 120° 吸附） |
| `acceleration` | 加速度向量（x/y/z） |
| `position` | AR 位置資料 |
| `ar_camera_pose` | AR 相機相對 Marker 位姿 |

#### 核心邏輯（步驟）

```
1. 客戶端連線 → 加入 clients Set，發送歡迎訊息
2. 收到訊息 → JSON 解析
3. 依 type 建立標準格式的 out 物件（加入 timestamp、clientId）
4. 廣播 out 給所有「其他」客戶端（排除發送者自身）
5. 回傳 ack 給發送者確認已收到
6. 連線關閉 / 錯誤 → 從 clients 移除
```

#### 健康檢查端點

| 路徑 | 說明 |
|------|------|
| `GET /health` | 基本狀態（運行時間、連線數） |
| `GET /api/status` | 詳細狀態（記憶體使用量） |
| `GET /api/ping` | 心跳確認（pong） |

---

### `GyroscopeReceiver.cs`（Unity WebSocket 接收器）

#### 功能說明
Unity 中的核心橋接腳本。負責連線到 WebSocket 伺服器、接收所有感測器訊息，並透過 **C# 事件（Event）** 通知其他腳本。就像一個「分電箱」，訊號進來後按類型分配給各個需要的腳本。

#### 核心參數（Inspector 設定）

| 欄位 | 說明 | 預設值 |
|------|------|--------|
| `serverUrl` | WebSocket 伺服器網址 | Railway 線上 URL |
| `autoConnect` | 啟動時自動連線 | `true` |
| `reconnectInterval` | 斷線後重連間隔 | `5` 秒 |
| `roomId` | 房間識別碼 | `"default-room"` |
| `role` | 此客戶端角色 | `"unity-receiver"` |
| `debugLog` | 是否顯示詳細 Log | `false` |

#### 對外公開的事件（供其他腳本訂閱）

```csharp
// 其他腳本可用這些事件接收資料：
GyroscopeReceiver.OnGyroscopeDataReceived  += HandleGyro;    // 陀螺儀
GyroscopeReceiver.OnAccelerationReceived   += HandleAcc;     // 加速度
GyroscopeReceiver.OnShakeDataReceived      += HandleShake;   // 搖晃
GyroscopeReceiver.OnSpinDataReceived       += HandleSpin;    // 旋轉
GyroscopeReceiver.OnSpinModeStatusReceived += HandleMode;    // 模式切換
GyroscopeReceiver.OnARCameraPoseReceived   += HandleARPose;  // AR 位姿
GyroscopeReceiver.OnPositionDataReceived   += HandlePos;     // 位置
GyroscopeReceiver.OnConnected              += HandleConnect; // 連線成功
GyroscopeReceiver.OnDisconnected           += HandleDiscon;  // 斷線
```

#### 核心邏輯（步驟）

```
1. Start() → 若 autoConnect=true 則呼叫 ConnectToServer()
2. ConnectToServer()：
   a. 建立 WebSocket 物件並設定回呼
   b. OnOpen  → 發送 join 訊息加入房間，觸發 OnConnected
   c. OnClose → 更新狀態，啟動 AutoReconnect 協程
   d. OnMessage → 解析 JSON → switch(type) → 觸發對應事件
3. Update() → 呼叫 DispatchMessageQueue()（NativeWebSocket 需要）
              → 偵測空白鍵 → 發送 spin_mode toggle 給網頁
4. AutoReconnect() → 每 reconnectInterval 秒嘗試重連
```

#### 特殊功能：空白鍵模式切換

在 Unity Editor 中按下**空白鍵**，會透過 WebSocket 發送 `spin_mode` 訊息，通知手機端切換旋鈕的吸附角度（90° ↔ 120°）。

---

### `AccelerometerBallEffect.cs`（加速度球體移動效果）

#### 功能說明
訂閱 `GyroscopeReceiver` 的加速度事件，讓掛載此腳本的 GameObject（例如一顆球）跟著手機的傾斜與推動而移動。停止施力後會自動回到中心位置，就像**水平儀中的氣泡**。

#### 核心參數（Inspector 設定）

| 欄位 | 說明 | 建議範圍 |
|------|------|---------|
| `centerPoint` | 移動的錨點（中心）| 任意 Transform |
| `sensitivity` | 加速度 → 位移的放大倍率 | `0.1 ~ 1.0` |
| `smoothSpeed` | 追蹤速度（越大越即時）| `5 ~ 20` |
| `inputFilterTime` | 低通濾波時間常數 | `0.03 ~ 0.1` 秒 |
| `movementAxesMask` | 哪些軸受影響（1=開/0=關）| `(1,1,0)` = XY 平面 |
| `maxOffset` | 最大偏移距離（米）| `1.0 ~ 5.0` |

#### 核心邏輯（步驟）

```
1. Start() → 訂閱 OnAccelerationReceived 和 OnGyroscopeDataReceived
2. HandleAcceleration(acc) → 儲存原始加速度到 rawAcceleration
3. HandleGyroscopeData(data) → 儲存上下移動值 pitchY = -data.unityY
4. Update() 每幀執行：
   a. 低通濾波 filteredAcceleration（消除感測器雜訊）
   b. 計算 targetOffset = filteredAcc × mask × sensitivity
   c. ClampMagnitude → 限制在 maxOffset 範圍內
   d. SmoothDamp → 平滑追蹤 targetOffset（currentOffset）
   e. Y 軸獨立：smoothedPitchY 來自 pitchY（上下移動）
   f. 更新 transform.localPosition = centerLocalPosition + currentOffset
5. 空白鍵 → 呼叫 Recalibrate() 重設中心點
```

---

## 4. 使用方式（How to Use）

### 前置需求

- **Node.js** 16+ 已安裝
- **Unity** 2021 LTS 以上
- Unity 專案已安裝 [NativeWebSocket](https://github.com/endel/NativeWebSocket) 套件
- 手機與電腦在**同一網路**（或使用 Railway 等雲端服務）

---

### Step 1：啟動 WebSocket 伺服器

```bash
# 在專案根目錄
npm install
node server.js
```

終端機出現以下訊息代表成功：
```
🚀 陀螺儀WebSocket伺服器啟動成功!
📱 靜態檔案服務: http://localhost:8080
🔌 WebSocket端點: ws://localhost:8080
```

---

### Step 2：設定 Unity 場景

1. 在場景中建立一個空的 GameObject，命名為 `GyroManager`
2. 將 `GyroscopeReceiver.cs` 拖到 `GyroManager` 上
3. 在 Inspector 設定：
   - `Server Url`：填入伺服器 WebSocket 網址（本機測試填 `ws://你的IP:8080`）
   - `Auto Connect`：勾選
   - `Debug Log`：開發階段建議勾選，正式版關閉

4. 建立一個球體 GameObject，命名為 `Ball`
5. 將 `AccelerometerBallEffect.cs` 拖到 `Ball` 上
6. 在 Inspector 設定：
   - `Center Point`：指定一個錨點 Transform（或留空使用球體初始位置）
   - `Sensitivity`：從 `0.3` 開始調整
   - `Smooth Speed`：建議 `10`
   - `Movement Axes Mask`：`(1, 1, 0)` 表示只在 XY 平面移動

---

### Step 3：開啟手機旋鈕頁面

1. 確認手機與伺服器在同一網路
2. 在手機瀏覽器輸入 `http://伺服器IP:8080`
3. 若要使用旋鈕功能，直接開啟 `SpinTest/index.html`（本機測試用）

> ⚠️ iOS Safari 需要 HTTPS 才能存取感測器。部署到 Railway 等 HTTPS 服務後即可正常運作。

---

### Step 4：測試是否成功

| 測試項目 | 預期結果 |
|---------|---------|
| 伺服器啟動 | 瀏覽器開 `http://localhost:8080/health` 顯示 JSON |
| Unity 連線 | Console 出現「🔌 WebSocket連接已建立」 |
| 手機搖晃 | Unity Console 出現「📳 收到搖晃數據」 |
| 手機傾斜 | `AccelerometerBallEffect` 的 Debug 欄位有數值變化 |
| 旋鈕操作 | Unity 的 `lastSpinAngle` 欄位更新 |

---

## 5. 原理解析（Core Concepts）

### 5.1 為什麼需要 WebSocket 中繼？

手機瀏覽器的感測器 API 是用 JavaScript 讀取的，而 Unity 是用 C# 執行的。這兩者**完全不同的執行環境**，無法直接呼叫對方的函式。

WebSocket 就像電話線：兩端都撥進同一個號碼（伺服器），就可以互相傳訊息了。

```
手機 JS ──撥進伺服器──> 伺服器廣播 ──> Unity C# 接收
```

---

### 5.2 低通濾波（Low-Pass Filter）是什麼？

加速度感測器的原始資料非常**抖動**，就像拿麥克風錄音會有很多雜訊。低通濾波的作用是**保留緩慢的變化，過濾掉快速的抖動**。

**直覺比喻**：想像你在看一個快速跳動的數字。低通濾波就像讓眼睛「慢慢移過去」，不隨著每個數字跳動，而是平滑地跟上趨勢。

**程式中的做法（指數加權平均）**：

```
alpha = 1 - exp(-deltaTime / filterTime)
filtered = Lerp(filtered, raw, alpha)
```

- `filterTime` 越小 → alpha 越大 → 更即時（更多原始訊號）
- `filterTime` 越大 → alpha 越小 → 更平滑（更多歷史訊號）

---

### 5.3 SmoothDamp 是什麼？為什麼比 Lerp 好？

`Vector3.Lerp(a, b, t)` 每幀固定比例靠近目標，**永遠追不上**（誤差越來越小但不為零），且沒有物理感。

`Vector3.SmoothDamp` 模擬**彈簧阻尼系統**：會先加速接近目標，快到時再減速煞車。結果是移動看起來有**慣性感**，更像真實物體。

**直覺比喻**：Lerp 像機器人走路，SmoothDamp 像人走路。

---

### 5.4 為什麼加速度儀靜止時還有數值？

加速度儀量測的是**所有力的總和**，包含重力（9.81 m/s²）。手機平放時，重力垂直向下，Z 軸就會讀到約 9.81。傾斜時，重力分量會分散到三個軸。

這就是為什麼手機傾斜時，`AccelerometerBallEffect` 的球會偏移——它的設計就是把重力分量當作「傾斜位移」來使用，不需要另外減去重力。

---

### 5.5 旋鈕的吸附（Snap）邏輯

```
使用者拖拽旋鈕 → 計算增量（currentAngle - startAngle）

如果 |增量| > 45°:
    吸附到 startAngle ± 90°（跨過中點就算完成一格）
    播放音效 + 震動

如果 |增量| ≤ 45°:
    回彈到 startAngle（沒轉夠，取消這次旋轉）
```

**直覺比喻**：就像撥號盤，轉過一半就算撥進下一格，沒轉到一半就彈回原位。

---

### 5.6 elastic.out 緩動是什麼？

GSAP 的 `elastic.out(amplitude, period)` 會讓動畫**超過目標值再彈回**，模擬彈簧被拉緊後的彈射效果。

- `amplitude = 1`：最大超出量（越大越誇張）
- `period = 0.3 ~ 0.4`：振盪週期（越小振得越快）

---

## 6. 技術棧（Tech Stack）

| 技術 | 版本 | 用途 |
|------|------|------|
| **Unity** | 2021 LTS+ | 3D 場景渲染、物件控制、C# 邏輯 |
| **NativeWebSocket** | latest | Unity 的 WebSocket 客戶端套件 |
| **Node.js + Express** | 16+ | HTTP 伺服器 + 靜態檔案服務 |
| **ws (WebSocket)** | latest | WebSocket 伺服器核心 |
| **GSAP** | 3.12.2 | 旋鈕旋轉動畫、Draggable 拖拽插件 |
| **jQuery** | 3.7.1 | DOM 操作輔助 |
| **Web Vibration API** | 瀏覽器內建 | 手機震動觸發 |
| **DeviceMotion API** | 瀏覽器內建 | 手機加速度感測器讀取 |
| **DeviceOrientation API** | 瀏覽器內建 | 手機陀螺儀角度讀取 |
| **Railway** | 雲端 | 部署 Node.js 伺服器（支援 WSS） |

---

## 7. 腳本組合建議（Integration）

### 基礎組合：球體移動

```
GyroscopeReceiver  ──OnAccelerationReceived──>  AccelerometerBallEffect
```

效果：手機傾斜 → 球跟著在 XY 平面偏移，放平後自動回中。

---

### 進階組合：旋鈕控制 + 物件旋轉

```
(手機) SpinTest 旋鈕頁面
    ↓ WebSocket spin 事件
GyroscopeReceiver
    ↓ OnSpinDataReceived
[自訂腳本] SpinController.cs  →  旋轉 Unity 物件
```

---

### 進階組合：搖晃觸發特效

```
GyroscopeReceiver
    ↓ OnShakeDataReceived (count, intensity)
[自訂腳本] ShakeEffect.cs → 播放粒子特效 / 鏡頭震動
```

---

### 進階組合：AR 相機同步

```
AR 頁面（ar_camera_pose 發送）
    ↓ WebSocket
GyroscopeReceiver
    ↓ OnARCameraPoseReceived (position, rotation, markerVisible)
[自訂腳本] ARCameraPoseApplier.cs → 同步 Unity 相機位姿
```

---

### 訂閱事件的寫法範例

```csharp
// 在你的自訂腳本中：
void OnEnable()
{
    GyroscopeReceiver.OnSpinDataReceived += OnSpin;
}

void OnDisable()
{
    GyroscopeReceiver.OnSpinDataReceived -= OnSpin;  // 必須取消訂閱！
}

void OnSpin(GyroscopeReceiver.SpinData data)
{
    Debug.Log($"旋轉角度: {data.angle}°");
    transform.Rotate(0, data.angle, 0);
}
```

> ⚠️ 記得在 `OnDisable` 或 `OnDestroy` 中取消訂閱，否則物件被刪除後會出現 NullReferenceException。

---

## 8. 常見問題（Troubleshooting）

### Q1：球體數值不動 / 加速度一直是 0

**可能原因：**
- `GyroscopeReceiver` 沒有正確連線到伺服器
- 手機頁面沒有成功傳送 `acceleration` 類型的訊息

**排查步驟：**
1. 確認 Unity Console 有「WebSocket連接已建立」
2. 開啟 `debugLog = true`，觀察是否收到任何訊息
3. 在瀏覽器 DevTools 確認 WebSocket 連線已建立，且有送出資料

---

### Q2：球體方向不對（往左傾斜但球往右跑）

**原因：** 手機座標系與 Unity 座標系方向不同。

**解決：** 在 `AccelerometerBallEffect` 的 `movementAxesMask` 或 `sensitivity` 加上負號來反轉軸向，或在發送端調整 x/y 的正負號。

---

### Q3：數值一直抖動 / 不穩定

**原因：** 感測器雜訊，或 `inputFilterTime` 太小。

**解決：**
- 增大 `inputFilterTime`（例如從 0.05 → 0.1 秒）
- 減小 `sensitivity`
- 確認手機本身沒有物理震動（放在桌上測試）

---

### Q4：旋鈕頁面在 iOS 上感測器不動

**原因：** iOS 13+ 需要使用者手動授權感測器權限，且必須在 **HTTPS** 環境下。

**解決：** 將伺服器部署到有 SSL 憑證的雲端服務（如 Railway），用 `https://` 開啟頁面，首次進入時同意感測器授權。

---

### Q5：Unity 連不到 WebSocket（本機測試）

**原因：** 防火牆阻擋，或 IP 位址填錯。

**解決：**
1. 確認 `serverUrl` 填的是電腦的**區域網路 IP**（如 `ws://192.168.1.5:8080`），而非 `localhost`
2. 暫時關閉防火牆，或開放 8080 埠
3. 手機和電腦必須在**同一 Wi-Fi**

---

### Q6：旋鈕吸附後角度值不對

**原因：** `lastSpinAngle` 是吸附後的角度（以起始角度為基準的增量），不是絕對角度。

**解決：** 若需要累計絕對角度，在自訂腳本中加總每次收到的 `SpinData.angle`。

---

## 9. 延伸方向（Advanced Ideas）

### 9.1 優化感測器穩定性

| 技術 | 效果 | 難度 |
|------|------|------|
| **Kalman Filter** | 最佳化的狀態估計，比低通濾波更準確 | ⭐⭐⭐ |
| **互補濾波（Complementary Filter）** | 結合陀螺儀積分與加速度計，減少漂移 | ⭐⭐ |
| **滑動平均（Moving Average）** | 簡單易實作，適合入門 | ⭐ |

### 9.2 功能延伸

| 想法 | 說明 |
|------|------|
| **手勢辨識** | 偵測特定加速度模式（畫圈、甩動），觸發特定動作 |
| **物理互動** | 把加速度接到 Rigidbody.AddForce，做真實的物理模擬 |
| **多人同步** | 多支手機同時控制不同物件 |
| **旋鈕調參** | 用旋鈕角度即時調整 Unity 場景的光照強度、音量等參數 |
| **AR 疊加** | 結合 AR Foundation，把 AR 追蹤資料映射到 Unity AR 相機 |
| **WebRTC 升級** | 改用 WebRTC Data Channel 降低延遲（目前架構已預留信令支援） |

### 9.3 架構優化

- **訊息壓縮**：高頻率的陀螺儀資料可以改用 MessagePack 二進制格式，減少網路流量
- **差分傳輸**：只傳送「與上次的差值」而非完整數值，節省頻寬
- **本地預測**：Unity 端根據速度預測下一幀位置，降低延遲感

---

## 附錄：訊息格式參考

### 發送到伺服器（手機 → Server）

```json
// 加速度
{
  "type": "acceleration",
  "data": {
    "acceleration": { "x": 0.12, "y": -0.05, "z": 9.81 },
    "magnitude": 9.812
  }
}

// 陀螺儀
{
  "type": "gyroscope",
  "alpha": 120.5,
  "beta": -15.2,
  "gamma": 3.8,
  "unityY": -0.5,
  "timestamp": 1710000000000
}

// 旋轉事件
{
  "type": "spin",
  "data": {
    "triggered": true,
    "angle": 90.0,
    "timestamp": 1710000000000
  }
}
```

### 伺服器廣播（Server → Unity）

```json
{
  "type": "acceleration",
  "data": {
    "acceleration": { "x": 0.12, "y": -0.05, "z": 9.81 },
    "magnitude": 9.812
  },
  "timestamp": 1710000001234,
  "clientId": 1
}
```

---

*文件版本：2026-03 | 系統：手機感測器 × WebSocket × Unity 互動控制系統*

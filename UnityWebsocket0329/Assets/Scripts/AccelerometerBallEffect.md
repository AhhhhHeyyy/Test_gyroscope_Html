# 手機感測器腳本說明

> 本專案透過 WebSocket / UDP 將手機感測器資料傳入 Unity，再由以下三支腳本分別控制物件的**旋轉**與**位移**。

---

## 腳本總覽

| 腳本 | 功能 | 類型 |
|------|------|------|
| `GyroToRotation.cs` | 讓物件跟著手機旋轉（陀螺儀） | Runtime |
| `RotateY90.cs` + `RotateY90Editor.cs` | 在 Inspector 中一鍵旋轉父物件 Y 軸 +90° | Editor Only |
| `AccelerometerBallEffect.cs` | 讓物件跟著手機加速度／傾斜移動 | Runtime |

---

## 1. GyroToRotation.cs — 陀螺儀物件旋轉

### 功能說明

掛在哪個物件上，該物件就會跟著手機方向旋轉。腳本從 `GyroscopeReceiver` 讀取由手機端預先計算好的四元數，並將其轉換成 Unity 左手座標系後，套用到物件的 `localRotation`。

### 放置方式

1. 在 Hierarchy 選取要跟著手機轉動的物件（例如攝影機或 3D 模型）。
2. 在 Inspector 點 **Add Component** → 搜尋 `GyroToRotation`，加入腳本。
3. 將場景中的 `GyroscopeReceiver` 物件拖曳到 Inspector 的 **Receiver** 欄位。

> ⚠️ 若 Receiver 未指定，腳本會靜默不動作。

### Inspector 數值說明

| 欄位 | 說明 |
|------|------|
| **Receiver** | 指向場景中的 `GyroscopeReceiver` 組件，作為感測器資料來源（**必填**） |
| **Alpha**（唯讀 Debug） | 手機 Z 軸旋轉角（指南針方向，0°~360°）；僅供觀察，不可在此修改 |
| **Beta**（唯讀 Debug） | 手機 X 軸傾斜角（前後仰，-180°~180°）；僅供觀察 |
| **Gamma**（唯讀 Debug） | 手機 Y 軸傾斜角（左右傾，-90°~90°）；僅供觀察 |

### 座標系轉換說明

瀏覽器的感測器座標系（右手系，X=東、Y=北、Z=上）與 Unity 左手系不同，腳本內部做了以下映射：

```
Browser 四元數 (qx, qy, qz, qw)
  → Unity localRotation = Quaternion(qx, -qz, qy, qw)
```

---

## 2. RotateY90.cs / RotateY90Editor.cs — 父物件 Y 軸旋轉

### 功能說明

在 Unity **Editor** 的 Inspector 中新增一個按鈕，按下後可讓物件在**本地空間（Local Space）沿 Y 軸旋轉 +90°**。因為 Unity 的父子關係，旋轉父物件同時也會影響所有子物件的方向。

> ⚠️ **Editor Only**：`RotateY90Editor.cs` 放在 `Assets/Editor/` 資料夾，只在 Unity Editor 中有效，**不會包進最終 Build**。腳本僅供場景擺設階段使用，不能在執行期動態旋轉。

> ⚠️ 目前只有 **+90°** 按鈕，沒有 -90° 功能。如需反向旋轉，請在 Inspector 的 Transform 欄位手動調整 Rotation Y，或請開發者另外實作 -90° 按鈕。

### 放置方式

1. 在 Hierarchy 選取要調整方向的**父物件**。
2. 在 Inspector 點 **Add Component** → 搜尋 `RotateY90`，加入腳本（`RotateY90.cs`）。
3. 加入後 Inspector 會自動顯示 **Rotate Y +90°** 按鈕（由 `RotateY90Editor.cs` 注入）。
4. 每按一次，物件本地 Y 軸旋轉 +90°，可按多次累加。

### Inspector 數值說明

`RotateY90.cs` 本身為空的 MonoBehaviour，**沒有任何可調整的數值**。所有功能由 `RotateY90Editor.cs` 以自訂 Inspector 按鈕的形式提供：

| 按鈕 | 說明 |
|------|------|
| **Rotate Y +90°** | 讓物件沿本地 Y 軸旋轉 +90°，支援 Undo（Ctrl+Z 可還原） |

---

## 3. AccelerometerBallEffect.cs — 加速度位移控制

### 功能說明

掛在哪個物件上，手機的加速度與傾斜就會影響該物件的移動，模擬「水平儀紅球」效果：

- **直立模式**（手機豎立）：以手機傾斜角（重力方向）控制物件在 XY 平面的位移
- **平放模式**（手機平躺）：以線性加速度（推力）控制物件在 XZ 平面的位移
- 腳本自動偵測手機姿態並切換模式，兩種模式的參數完全獨立

### 放置方式

1. 在 Hierarchy 選取要受加速度影響的物件（例如球體）。
2. 在 Inspector 點 **Add Component** → 搜尋 `AccelerometerBallEffect`，加入腳本。
3. 確保場景中已有 `GyroscopeReceiver` 且 WebSocket / UDP 連線正常。
4. **（選填）** 將場景中的錨點物件拖曳到 **Center Point** 欄位，指定移動的中心位置。
5. **（選填）** 建立一個 UI Button，在 `On Click()` 中呼叫本腳本的 `Recalibrate()` 方法，作為手動校正按鈕。

### 使用流程

```
1. 將手機平放（或維持要使用的姿態）
2. 在 Unity Editor 按下 Play
3. 等待約 1.5 秒（autoCalibrationDelay），腳本自動完成初始校正
   → 校正完成前物件鎖定不動
4. 校正完成後，移動手機即可控制物件
5. 若物件位置偏移（漂移），可再次校正：
   - 按 UI 上的校正按鈕（呼叫 Recalibrate()）
   - Editor 中按 Space 鍵
   - 實機上雙指同時點擊螢幕
```

---

### Inspector 數值說明

#### 中心點（Center Point）

| 欄位 | 說明 |
|------|------|
| **Center Point** | 物件移動的錨點 Transform。若留空，則以 `Start()` 時物件的本地位置為中心 |

---

#### 自動校正（Auto Calibration）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Auto Calibration Delay** | `1.5` 秒 | 收到第一筆感測器資料後，等待幾秒才自動校正。目的是讓低通濾波先穩定，避免校正到雜訊值。範圍 0~5 秒，`0` = 立即校正 |
| **Has Calibrated**（唯讀） | `false` | 是否已完成初始校正。未完成前物件鎖定於原點不動 |

---

#### 平放 / 直立切換（Mode Switch）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Flatness Threshold** | `0.7` | 判斷手機是否平放的閾值（0~1）。數值代表「重力在手機螢幕法線方向的比例」，超過此值視為平放。建議範圍 0.6~0.8，數值越小越容易切換成平放模式 |
| **Phone Is Flat**（唯讀） | — | 目前判斷結果：`true` = 平放模式，`false` = 直立模式 |
| **Mode Switch Debounce Time** | `0.3` 秒 | 防抖時間（秒）：感測器判斷新模式後，必須穩定超過此秒數才真正切換，防止快速揮動時頻繁抖動。建議 0.2~0.4 秒 |
| **Mode Switch Transition Duration** | `0.5` 秒 | 模式切換時，舊位置到新位置的跳動補償淡出時間（秒）。值越大切換越平滑，但響應較慢 |

---

#### 直立模式設定（Upright Settings）

手機豎立時使用的參數。物件在 **XY 平面**（左右 + 上下）移動。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Sensitivity** | `0.3` | 加速度輸入轉換為位移的縮放倍率。越大越靈敏，手機稍微傾斜物件就移動很遠 |
| **Smooth Speed** | `10` | 位移平滑速度（1~30）。越大越即時、越小越滑順有慣性感 |
| **Input Filter Time** | `0.05` 秒 | 感測器輸入的低通濾波時間常數（0.01~0.5 秒）。越小越即時、越大越平滑。建議 0.03~0.1 |
| **Movement Axes Mask** | `(1, 1, 0)` | 各軸是否啟用（1=開，0=關）。直立模式預設 X=開（左右）、Y=開（上下）、Z=關 |
| **Axis Flip** | `(1, 1, -1)` | 各軸方向翻轉（1=正常，-1=反向）。若物件移動方向與手機傾斜方向相反，將對應軸改為 `-1` |
| **Axis Deadzone** | `(0.3, 0.3, 0.3)` | 各軸死區（m/s²）。低於此值的輸入歸零，消除手機靜止時的微小漂移。值越大越能消抖，但反應也較遲鈍 |
| **Axis Scale** | `(1, 1, 1)` | 各軸移動幅度的額外縮放。在 Sensitivity 之後再做微調，可單獨放大或縮小某軸的移動範圍 |
| **Max Offset Per Axis** | `(3, 3, 3)` | 各軸允許的最大偏移距離（米）。各軸獨立限制，不會互相壓縮。超過此值後物件不再繼續移動 |

---

#### 平放模式設定（Flat Settings）

手機平躺時使用的參數。物件在 **XZ 平面**（左右 + 前後）移動。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Sensitivity** | `0.08` | 平放模式靈敏度較低，因為線性加速度輸入值通常比重力傾斜大，需要較小的倍率 |
| **Smooth Speed** | `10` | 同直立模式 |
| **Input Filter Time** | `0.05` 秒 | 同直立模式 |
| **Movement Axes Mask** | `(1, 0, 1)` | 平放模式 Y=關（垂直方向無意義）、X=開（左右）、Z=開（前後） |
| **Axis Flip** | `(1, 1, -1)` | 同直立模式，可各軸獨立翻轉 |
| **Axis Deadzone** | `(0.2, 0.2, 0.2)` | 平放模式死區稍小，因為線性加速度噪音相對較低 |
| **Axis Scale** | `(1, 1, 1)` | 同直立模式 |
| **Max Offset Per Axis** | `(3, 3, 3)` | 同直立模式 |

---

#### 輸出平滑（Output Smoothing）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Position Filter Time** | `0.05` 秒 | 最終輸出位置的低通濾波時間常數（0~0.3 秒）。在 SmoothDamp 之後再做一次 EMA 平滑，消除高頻抖動。`0` = 關閉此濾波。建議 0.03~0.08 |

---

#### 水平儀數值（Level Axis，唯讀 Debug）

| 欄位 | 說明 |
|------|------|
| **Level Axis X** | 校正後 X 軸加速度（負=左傾，正=右傾） |
| **Level Axis Y** | 校正後 Y 軸加速度（負=前傾，正=後傾） |
| **Roll Deg** | Roll 角（繞 Z 軸，左右傾斜角度，單位度） |
| **Pitch Deg** | Pitch 角（繞 X 軸，前後傾斜角度，單位度） |

---

#### 陀螺儀原始輸入 Debug（唯讀）

| 欄位 | 說明 |
|------|------|
| **Debug G Device** | 裝置座標系重力向量。直立時 z≈0，平放時 z≈±9.81 |
| **Debug Flatness Ratio** | `|gDevice.z| / 9.81`，超過 Flatness Threshold 即切為平放 |
| **Debug Qx/Qy/Qz/Qw** | 當前收到的四元數原始值 |
| **Debug Linear Acc Input** | `HandleAcceleration` 收到的線性加速度（已去除重力） |
| **Debug Euler Angles** | WebSocket 備用模式的 alpha/beta/gamma，四元數模式下無效 |

---

#### 調試數值（Debug，唯讀）

| 欄位 | 說明 |
|------|------|
| **Debug Raw Acceleration** | 感測器直接輸入的加速度（未經濾波） |
| **Debug Filtered Acceleration** | 低通濾波後的加速度 |
| **Debug Calibrated Acceleration** | 校正時記錄的基準值（Tare 值） |
| **Debug Debiased Acceleration** | `Filtered - Calibrated`，實際用於計算位移的值；校正後靜止時接近 (0,0,0) |
| **Debug Target Offset** | 本幀計算出的目標偏移量（未平滑） |
| **Debug Current Offset** | SmoothDamp 平滑後的實際偏移量 |
| **Debug Transition Progress** | 模式切換補償的進度（0=剛切換，1=補償完成） |
| **Debug Actual Position** | 物件當前的實際 localPosition |

---

#### 切換事件記錄（唯讀）

| 欄位 | 說明 |
|------|------|
| **Debug Last Switch Dir** | 最後一次切換的方向（「直立→平放」或「平放→直立」） |
| **Debug Time Since Last Switch** | 距上次切換經過的秒數（-1 = 尚未切換） |
| **Debug Switch Start Dist** | 切換瞬間的位置跳動量（米），越小代表過渡越平滑 |
| **Debug Max Frame Jump** | 過渡期間最大單幀位置跳動（米） |
| **Debug Transition Remaining** | 過渡剩餘比例（1=剛切換，0=完成） |

---

## 資料處理管線（完整流程）

```
手機感測器
  │
  ├─ 四元數 (qx,qy,qz,qw) ──→ HandleGyroscopeData()
  │     ├─ 計算 gDevice（裝置座標系重力）
  │     ├─ 判斷 rawPhoneIsFlat（是否平放）
  │     └─ 直立模式：rawAcceleration.x/y = gDevice.x/z
  │
  └─ 線性加速度 (ax,ay,az) ──→ HandleAcceleration()
        ├─ 平放模式：worldAcc = q * acc → rawAcceleration = (worldAcc.x, worldAcc.z, worldAcc.y)
        └─ 直立模式：rawAcceleration.z = worldAcc.y（前後位移）

Update() 每幀處理：
  rawAcceleration
    → [低通濾波 inputFilterTime]  → filteredAcceleration
    → [減去校正基準 calibrated]   → debiased
    → [axisFlip 各軸翻轉]
    → [ApplyDeadzone 死區歸零]
    → [movementAxesMask 遮罩 × sensitivity]  → targetOffset
    → [Clamp maxOffsetPerAxis]
    → [SmoothDamp smoothSpeed]               → currentOffset
    → [× axisScale] + centerLocalPosition   → proposedPosition
    → [切換補償淡出 modeSwitchTransitionOffset]
    → [輸出低通濾波 positionFilterTime]      → smoothedPosition
    → transform.localPosition
```

---

## 常見問題

**Q：物件一直在抖動**
→ 增大 `Axis Deadzone`（消除靜止漂移），或增大 `Input Filter Time` / `Position Filter Time`（增加平滑）。

**Q：物件移動方向相反**
→ 將對應軸的 `Axis Flip` 從 `1` 改為 `-1`。

**Q：直立/平放模式切換太敏感（稍微傾斜就切換）**
→ 增大 `Flatness Threshold`（建議調到 0.8），或增大 `Mode Switch Debounce Time`。

**Q：切換模式時物件位置跳動**
→ 增大 `Mode Switch Transition Duration` 讓補償動畫更長；或減小兩種模式的 `Sensitivity` 差異。

**Q：校正後物件仍會慢慢漂移**
→ 增大 `Axis Deadzone`；或在手機靜止時重新按校正。

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

- **直立模式**（手機豎立）：以手機傾斜角（重力方向）控制物件在 XY 平面的位移；Z 軸由線性加速度（前後推力）驅動
- **平放模式**（手機平躺）：以重力傾斜（gDevice）為主、線性加速度混合為輔，控制物件在 XZ 平面的位移
- 腳本自動偵測手機姿態並切換模式，兩種模式的參數完全獨立

### 座標系映射

```
Android GAME_ROTATION_VECTOR → gDevice = Inverse(q) * (0,0,-g)

直立模式：
  Unity X = gDevice.x    （左右傾斜，重力投影，持續量）
  Unity Y = gDevice.z    （前後傾斜，重力投影，持續量）
  Unity Z = worldAcc.y   （前後推力，線性加速度，瞬時量）

平放模式：
  Unity X = gDevice.x + linX * flatLinearBlendX
  Unity Z = gDevice.y + clamp(linZ) * flatLinearBlendZ
  （傾斜為持續量，推力為瞬時量；混合比例可調）
```

### 放置方式

1. 在 Hierarchy 選取要受加速度影響的物件（例如球體）。
2. 在 Inspector 點 **Add Component** → 搜尋 `AccelerometerBallEffect`，加入腳本。
3. 確保場景中已有 `GyroscopeReceiver` 且 WebSocket / UDP 連線正常。
4. **（選填）** 將場景中的錨點物件拖曳到 **Center Point** 欄位，指定移動的中心位置。
5. **（選填）** 建立一個 UI Button，在 `On Click()` 中呼叫本腳本的 `Recalibrate()` 方法，作為手動校正按鈕。

### 使用流程

```
1. 將手機維持在要使用的姿態（直立或平放）
2. 在 Unity Editor 按下 Play
3. 等待約 1.5 秒（autoCalibrationDelay），腳本自動完成初始校正
   → 校正完成前物件鎖定不動
4. 校正完成後，移動手機即可控制物件
5. 若物件位置偏移（漂移），可再次校正：
   - 按 Game 視窗中的「校正」按鈕（呼叫 Recalibrate()）
   - Editor 中按 Space 鍵
   - 實機上雙指同時點擊螢幕
6. 首次使用建議先執行「嚮導校正」，自動偵測最佳參數
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

手機豎立時使用的參數。物件在 **XYZ 三軸**移動（X=左右、Y=前後傾斜、Z=前後推力）。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Sensitivity** | `(0.3, 0.3, 0.3)` | 各軸感應靈敏度（sensor m/s² → targetOffset 單位）。由嚮導自動計算：`sensitivity.x = maxOffsetPerAxis.x / usableRange` |
| **Smooth Speed** | `10` | 位移平滑速度（1~30）。越大越即時、越小越滑順有慣性感。直立模式使用 SmoothDamp |
| **Input Filter Time** | `0.05` 秒 | 感測器輸入的低通濾波時間常數（0.01~0.5 秒）。越小越即時、越大越平滑。建議 0.03~0.1 |
| **Movement Axes Mask** | `(1, 1, 1)` | 各軸是否啟用（1=開，0=關）。直立模式 X=左右、Y=前後傾斜、Z=前後推力 |
| **Axis Flip** | `(1, 1, -1)` | 各軸方向翻轉（1=正常，-1=反向）。若物件移動方向與手機傾斜方向相反，將對應軸改為 `-1`。由嚮導自動偵測 |
| **Axis Deadzone** | `(0.3, 0.3, 0.3)` | 各軸死區（m/s²）。低於此值的輸入歸零，消除手機靜止時的微小漂移。由嚮導自動計算（= 3σ 噪聲標準差） |
| **Axis Scale** | `(1, 1, 1)` | 各軸移動幅度的額外縮放。在 Sensitivity 之後再做微調，可單獨放大或縮小某軸的移動範圍。**嚮導不修改此欄位** |
| **Max Offset Per Axis** | `(3, 3, 3)` | 各軸允許的最大偏移距離（米）。各軸獨立限制，不會互相壓縮。**嚮導以此反推 sensitivity** |
| **Min Output Step** | `(0, 0, 0)` | 輸出端最小有效位移（Unity 單位）。`scaledOffset` 低於此值歸零，消除靜止微抖。由嚮導自動計算 |
| **Swap XZ** | `false` | 交換 X 與 Z 軸的輸入來源。嚮導偵測到手機旋轉 90° 時自動設定為 `true` |

---

#### 平放模式設定（Flat Settings）

手機平躺時使用的參數。物件在 **XZ 平面**（左右 + 前後）移動。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Flat Linear Blend X** | `0` | 平放 X 軸線性加速度（linX）混合比例（0~5）。`gDevice.x` 已是穩定傾斜投影，加入 linX 只帶入手部晃動噪聲，建議保持 `0` |
| **Flat Linear Blend Z** | `0.5` | 平放 Z 軸線性加速度（linZ）混合比例（0~5）。`gDevice.y` 在平放時訊號弱，需要 linZ 補充推力感。建議 0.3~0.7 |
| **Flat Lin Z Clamp** | `8` m/s² | linZ 上限截斷，防止快速甩動時尖峰暴衝。建議 6~12 |
| **Sensitivity** | `(0.3, 0.3, 0.3)` | 各軸感應靈敏度，由嚮導自動計算 |
| **Smooth Speed** | `7` | 平放模式使用 EMA（指數移動平均）追蹤，不會累積速度也不過衝 |
| **Input Filter Time** | `0.06` 秒 | 同直立模式 |
| **Movement Axes Mask** | `(1, 0, 1)` | 平放模式 Y=關（垂直方向無意義）、X=開（左右）、Z=開（前後） |
| **Axis Flip** | `(1, 1, -1)` | 同直立模式，由嚮導自動偵測 |
| **Axis Deadzone** | `(0.2, 0.2, 0.2)` | 同直立模式，由嚮導自動計算 |
| **Axis Scale** | `(1, 1, 1)` | 同直立模式，**嚮導不修改** |
| **Max Offset Per Axis** | `(3, 3, 3)` | 同直立模式，**嚮導以此反推 sensitivity** |
| **Min Output Step** | `(0, 0, 0)` | 同直立模式，由嚮導自動計算 |
| **Swap XZ** | `false` | 同直立模式，由嚮導自動偵測 |

---

#### 平放模式重力中心補償（Flat Gravity Correction）

解決 `GAME_ROTATION_VECTOR` 純陀螺儀積分的長時間漂移問題：手機靜止時，gDevice.x/y 會緩慢偏離 0，導致球不回中心。啟用後，靜止期間自動緩慢修正。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Enable Flat Gravity Correction** | `false` | 啟用後：平放靜止時，自動緩慢修正 XZ 漂移（Tare 值往 filtered 靠攏）。注意：啟用過快的補償會把手機慢速傾斜也當成漂移吸收 |
| **Flat Gravity Correction Time** | `30` 秒 | 修正時間常數（秒），越大越保守、越小越積極。建議 20~60。太小會吃掉慢速傾斜訊號 |

> 此功能對應 Debug 的 **dbPipe_idleRatio**：1=補償正在進行，0=已暫停（手機在動）。

---

#### 輸出平滑（Output Smoothing）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Position Filter Time** | `0.05` 秒 | 最終輸出位置的低通濾波時間常數（0~0.3 秒）。在 SmoothDamp/EMA 之後再做一次 EMA 平滑，消除高頻抖動。`0` = 關閉此濾波。建議 0.03~0.08 |

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
| **Debug G Device** | 裝置座標系重力向量 `gDevice = Inverse(q)*(0,0,-g)`。直立時 z≈0，平放時 z≈±9.81 |
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
| **Debug Current Offset** | SmoothDamp/EMA 平滑後的實際偏移量 |
| **Debug Transition Progress** | 模式切換補償的進度（0=剛切換，1=補償完成） |
| **Debug Actual Position** | 物件當前的實際 localPosition |
| **Debug Scaled Before Filter** | `scaledOffset` 套用 minOutputStep 前（原始縮放後位移） |
| **Debug Scaled After Filter** | `scaledOffset` 套用 minOutputStep 後（實際驅動位置的值） |
| **Debug Min Output Step** | 目前模式的 minOutputStep（輸出端死區閾值） |

---

#### 平放 XZ 管線（調試：來回移動時觀察，唯讀）

平放模式專用，追蹤從原始輸入到目標偏移的完整資料流。

| 欄位 | 說明 |
|------|------|
| **Pipe Log Interval** | Console 輸出間隔（秒）；`0` = 關閉定時輸出 |
| **dbPipe Raw** | `rawAcceleration.x/z`（輸入濾波前，來自 gDevice.x / gDevice.y） |
| **dbPipe Filtered** | `filteredAcceleration.x/z`（感測器 EMA 濾波後） |
| **dbPipe Tare** | `calibratedAcceleration.x/z`（Tare 參考點，重力補償活躍時此值會緩慢移動） |
| **dbPipe Pre Swap** | `debiased.x/z`（swap 前 = filtered − tare） |
| **dbPipe Post Swap** | `debiased.x/z`（swap 後 = 實際驅動 Unity XZ 的訊號） |
| **dbPipe After Dz** | flip + 死區後的 x/z（`0` = 在死區內，球不動） |
| **dbPipe Target** | `targetOffset.x/z`（球的目標位置，EMA 前） |
| **dbPipe Ema Alpha** | 每幀 EMA 追蹤速率（smoothSpeed=7 @ 60fps ≈ 0.10） |
| **dbPipe Idle Ratio** | 重力補償活躍度：1=Tare 正在往 filteredAcc 靠（球被拉回中心）；0=已暫停 |

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

#### 自動校正嚮導（Calibration Wizard）

7 步全自動流程，偵測 `axisFlip`、`axisDeadzone`、`sensitivity`、`swapXZ`、`minOutputStep` 並寫入 settings。`maxOffsetPerAxis` 與 `axisScale` 由使用者自行設定，**嚮導不修改**。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Wizard Collect Duration** | `2.5` 秒 | 每個採樣階段的持續時間（1~5 秒）。越長樣本越多、結果越穩定 |
| **Debug Tilt Angle Deg**（唯讀） | — | 目前傾斜角度：X=左右傾斜、Z=前後傾斜（度）。嚮導 MaxGesture 步驟時即時觀察 |
| **Show Wizard Button** | `true` | 是否在 Game 視窗顯示「嚮導校正」按鈕 |
| **Wizard Status Text**（唯讀） | — | 目前嚮導狀態文字 |
| **Wizard Upright Flip**（唯讀） | — | 嚮導偵測到的直立模式 axisFlip 結果（確認套用前的預覽值） |
| **Wizard Flat Flip**（唯讀） | — | 嚮導偵測到的平放模式 axisFlip 結果 |
| **Wizard Min Peak Display**（唯讀） | — | PushForward 偵測到的最小有意義衝程幅度（Z 軸，m/s²） |
| **Wizard Max Peak Display**（唯讀） | — | PushForward 偵測到的最大衝程幅度（Z 軸，m/s²） |

##### 嚮導步驟流程

```
步驟 1 (UprightBaseline)  — 直立靜止 → 計算 tare + 噪聲死區
步驟 2 (UprightMaxGesture) — 直立往右傾最大舒適角度 → 偵測 axisFlip.x + swapXZ + sensitivity.x
步驟 3 (UprightForward)   — 直立前後來回移動 → 偵測 axisFlip.z + sensitivity.z + 精修 deadzone.z
步驟 4 (FlatTransition)   — 等待手機平放（10 秒逾時可跳過）
步驟 5 (FlatBaseline)     — 平放靜止 → 計算 tare + 噪聲死區
步驟 6 (FlatMaxGesture)   — 平放往右傾最大舒適角度 → 同步驟 2
步驟 7 (FlatForward)      — 平放前後推動 → 同步驟 3 → 確認套用
```

> 每步驟結束後顯示摘要，確認後按「確認套用」才真正寫入設定並執行 Recalibrate()。

---

#### 校正按鈕（Game 視窗）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Show Calibration Button** | `true` | 是否在 Game 視窗顯示「校正」按鈕 |
| **Calibration Button Position** | `(10, 200)` | 按鈕左上角位置（像素，左上角為原點） |
| **Calibration Button Size** | `(120, 50)` | 按鈕尺寸（像素） |

---

#### 偏移量 HUD（Game 視窗）

即時顯示物件在移動範圍內的位置：2D 方格（X 橫軸、Z 縱軸）+ 側邊 Y 直條。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Show Offset HUD** | `true` | 是否在 Game 視窗顯示偏移量指示器 |
| **Hud Position** | `(10, 10)` | HUD 左上角位置（像素） |
| **Hud Size** | `100` px | HUD 方格邊長（60~200 像素） |

---

#### 軸縮放即時調整面板（Game 視窗）

嚮導校正後用來微調各軸移動距離，不需要重新跑嚮導。

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Show Scale Tuner** | `true` | 是否在 Game 視窗顯示 axisScale 調整面板 |
| **Scale Tuner Position** | `(140, 10)` | 調整面板左上角位置（像素），預設在偏移 HUD 右側 |
| **Scale Tuner Step** | `0.1` | 每次按 + / − 的調整幅度（0.01~1） |

---

#### 軸示意線（Scene / Game 視窗）

| 欄位 | 預設值 | 說明 |
|------|--------|------|
| **Show Axis Gizmos** | `true` | 是否顯示 XYZ 軸示意線（紅=X，綠=Y，藍=Z） |
| **Axis Gizmo Length** | `1` 米 | 軸示意線長度（0.1~5 米） |

---

## 資料處理管線（完整流程）

```
手機感測器
  │
  ├─ 四元數 (qx,qy,qz,qw) ──→ HandleGyroscopeData()
  │     ├─ 計算 gDevice = Inverse(q)*(0,0,-g)
  │     ├─ 判斷 rawPhoneIsFlat（|gDevice.z|/g >= flatnessThreshold）
  │     ├─ 直立模式：rawAcceleration.x = gDevice.x
  │     │           rawAcceleration.y = gDevice.z（前後傾斜，防抖期凍結）
  │     └─ 平放模式：rawAcceleration.x = gDevice.x + linX * flatLinearBlendX
  │                 rawAcceleration.z = gDevice.y + clamp(linZ) * flatLinearBlendZ
  │
  └─ 線性加速度 (ax,ay,az) ──→ HandleAcceleration()
        ├─ 直立模式：worldAcc = q * acc → rawAcceleration.z = worldAcc.y
        └─ 平放模式：worldAcc = q * acc → _linX/_linZ 暫存
                   （HandleGyroscopeData 混合使用）

Update() 每幀處理：
  [模式防抖] rawPhoneIsFlat 穩定 modeSwitchDebounceTime → 更新 phoneIsFlat
  [切換處理] 模式切換時立即重設 filteredAcceleration 為對應 Tare，
            並記錄跳動量準備淡出補償
  rawAcceleration
    → [低通濾波 inputFilterTime]     → filteredAcceleration
    → [減去校正基準 calibrated]      → debiased
    → [平放 XZ 重力補償（選用）]     → debiased / calibrated 緩慢修正
    → [swapXZ 軸交換（嚮導偵測）]   → debiased
    → [axisFlip 各軸翻轉]           → flipped
    → [ApplyDeadzone 死區歸零]      → deadzoned
    → [movementAxesMask × sensitivity] → targetOffset
    → [Clamp maxOffsetPerAxis]
    → [SmoothDamp（直立）/ EMA（平放）] → currentOffset
    → [× axisScale]                 → scaledOffset
    → [minOutputStep 輸出端死區]    → scaledOffset
    + centerLocalPosition           → proposedPosition
    → [切換補償淡出 SmoothStep]     → basePosition
    → [輸出低通濾波 positionFilterTime] → smoothedPosition
    → transform.localPosition
```

---

## 常見問題

**Q：物件一直在抖動**
→ 增大 `Axis Deadzone`（消除靜止漂移），或增大 `Input Filter Time` / `Position Filter Time`（增加平滑），或執行嚮導校正讓死區自動配置。

**Q：物件移動方向相反**
→ 將對應軸的 `Axis Flip` 從 `1` 改為 `-1`，或執行嚮導校正自動偵測方向。

**Q：直立/平放模式切換太敏感（稍微傾斜就切換）**
→ 增大 `Flatness Threshold`（建議調到 0.8），或增大 `Mode Switch Debounce Time`。

**Q：切換模式時物件位置跳動**
→ 增大 `Mode Switch Transition Duration` 讓補償動畫更長；或減小兩種模式的 `Sensitivity` 差異。

**Q：校正後物件仍會慢慢漂移**
→ 增大 `Axis Deadzone`；或在手機靜止時重新按校正；或啟用 `Enable Flat Gravity Correction`（平放模式）。

**Q：平放時物件對前後推動沒有反應**
→ 增大 `Flat Linear Blend Z`（建議 0.3~0.7），此值控制線性加速度對 Z 軸的貢獻比例。

**Q：嚮導校正的 sensitivity 跑出很大的數字（>10）**
→ 代表 `Max Offset Per Axis` 設定很大但傾斜動作很小；先確認 `Max Offset Per Axis` 是合理的移動距離（例如 3 米），再重新執行嚮導。

**Q：嚮導說「動作不夠明顯」**
→ MaxGesture 步驟需要傾斜 15°~45°。觀察 `Debug Tilt Angle Deg.x`，調整到約 30° 再保持靜止讓採樣完成。

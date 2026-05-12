# 缺陷修復紀錄

## 基本資訊

| 欄位 | 內容 |
|------|------|
| **日期** | 2026-04-13 |
| **問題類型** | 缺陷修復 / 演算法優化 |
| **嚴重程度** | Major |
| **狀態** | 已修復 |
| **受影響模組/腳本** | `UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs` |
| **關聯 Issue/Ticket** | N/A |

---

## 問題描述 / 需求背景

在手機直立（Upright）與平放（Flat）模式切換的瞬間，3D 物件的 Transform position 在 X / Z 軸會出現約 8～17 單位的瞬間跳變（Transient Discontinuity），體感明顯。

此外，使用者希望能隨時以當前手機姿勢作為位置原點，進行動態校正（Tare 功能）。

---

## 根本原因分析（Root Cause Analysis）

### 問題拆解

1. **問題一：rawAcceleration 數值來源驟變**
   - **現象**：切換瞬間 filteredAcceleration 尚未追上新模式的目標值
   - **作用機制**：直立模式使用重力向量（靜態，≈9.81 分量）；平放模式使用線性加速度（動態，靜止≈0），兩者量級與物理意義完全不同
   - **結果**：切換後殘值被 SmoothDamp 以舊模式速度繼續推移，產生彈射感

2. **問題二：movementAxesMask 軸遮罩翻轉**
   - **現象**：直立模式 mask=(1,1,0)，平放模式 mask=(1,0,1)，Y 軸與 Z 軸在切換瞬間對調
   - **作用機制**：切換前 currentOffset.Y 有殘值，切換後 Y 被關閉、Z 被打開，舊殘值分佈在錯誤軸上
   - **結果**：物件位置瞬間跳至非預期位置

3. **問題三：Hysteresis 缺失導致邊界震盪**
   - **現象**：手機在 flatnessThreshold 附近微幅移動時，phoneIsFlat 高頻切換
   - **結果**：物件在兩種模式間快速閃爍

4. **問題四：重力偏置（Gravity Bias）**
   - **現象**：使用者將 uprightSettings.movementAxesMask.Z 設為 1，但直立模式下 rawAcceleration.z = -gDevice.y ≈ ±9.81（常數）
   - **作用機制**：該值代表「手機有多垂直」，而非「手機移動了多少」，靜止時仍產生巨大 Z 軸偏移
   - **結果**：靜止直立時 Z 軸偏移 ≈ -8 ～ -17 單位

### 最根本原因

> 「有重力參考系（靜態角度位置）」與「無重力慣性系（動態線性位移）」在 45° 邊界上的數值斷層，以及缺少切換緩衝機制。

---

## 解決方案 / 修改內容

### 方案概覽

採用**方案一（Hysteresis）+ 方案二（State Reset + Cooldown）+ 動態校正（Tare）**組合。

### 關鍵代碼變更

**1. Hysteresis 磁滯邏輯（HandleGyroscopeData）**

```csharp
// 修改前
phoneIsFlat = Mathf.Abs(gDevice.z) / g >= flatnessThreshold;

// 修改後
float flatnessValue = Mathf.Abs(gDevice.z) / g;
float hysteresisLow = Mathf.Max(0f, flatnessThreshold - hysteresisGap);
if (phoneIsFlat)
    phoneIsFlat = flatnessValue >= hysteresisLow;
else
    phoneIsFlat = flatnessValue >= flatnessThreshold;
```

**2. 模式切換偵測 + 冷卻 + 重置（Update）**

```csharp
// 新增
if (phoneIsFlat != lastFlatState)
{
    lastFlatState = phoneIsFlat;
    if (Time.time > lastSwitchTime + switchCooldown)
    {
        ResetMovementState();
        lastSwitchTime = Time.time;
    }
}
```

**3. 動態校正基準（Tare）**

```csharp
// Update 中
Vector3 debiased = filteredAcceleration - calibratedAcceleration;

// Recalibrate() 中
calibratedAcceleration = filteredAcceleration; // 當前姿勢定為新原點
```

**4. ResetMovementState 同時清除校正基準**

```csharp
calibratedAcceleration = Vector3.zero; // 模式切換時清除舊校正
```

### 關聯 Git Commits

| Commit Hash | 說明 |
|-------------|------|
| `9a292f4` | 校正切換問題 20260413（本次修改前的最新版本） |

---

## 驗證清單

- [ ] 直立靜止：三軸接近 0（校正後）
- [ ] 平放靜止：三軸接近 0（校正後）
- [ ] 直立↔平放切換：無明顯跳變
- [ ] 邊界（45°）微幅移動：不再高頻閃爍
- [ ] 按 Space 校正：當前姿勢立即成為原點
- [ ] 校正後切換模式：舊校正清除，新模式重新歸零

---

## 影響評估

| 面向 | 說明 |
|------|------|
| **影響範圍** | 僅 AccelerometerBallEffect.cs，不影響其他腳本 |
| **向下相容性** | Inspector 新增兩個 SerializeField（hysteresisGap、switchCooldown），預設值安全 |
| **潛在副作用** | 模式切換時校正基準自動清除，使用者需在新模式重新按 Space 校正 |
| **效能影響** | 無（每幀僅新增一次 Vector3 減法） |

---

## 時間軸

| 時間 | 事件 |
|------|------|
| 2026-04-13 | 問題確認：直立平放切換導致位置跳變 |
| 2026-04-13 | 根本原因分析完成 |
| 2026-04-13 | 實作 Hysteresis + Reset + Cooldown + Tare |
| 2026-04-13 | 代碼修改完成，待實機驗證 |

---

## 經驗教訓（Lessons Learned）

1. **感測器融合邊界問題**：不同物理量（重力 vs 線性加速度）在模式切換點的量值斷層，必須用狀態重置或過渡混合來處理
2. **Hysteresis 是必要的**：任何基於閾值的狀態機都應加入磁滯區間，避免邊界震盪
3. **校正基準的作用域**：動態校正（Tare）需明確定義在模式切換時是否應保留或清除

---

## 後續任務

- [ ] 實機測試上述六項驗證
- [ ] 若切換仍有輕微跳變，考慮加入 lerp 過渡混合（方案三）

---

## 標籤

`#感測器融合` `#模式切換` `#加速度校正` `#Unity` `#Android`

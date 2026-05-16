# 除錯記錄

## 基本資訊

| 欄位 | 內容 |
| ---- | ---- |
| **日期** | 2026-05-16 |
| **問題類型** | 除錯修復 |
| **嚴重程度** | Major |
| **狀態** | 已修復 |
| **涉及檔案/模組** | `AccelerometerBallEffect.cs` |
| **相關 Issue/Ticket** | N/A |

---

## 問題描述

- **現象 A（X 軸中心點）**：平放模式下，手往身體右側移動並**保持靜止傾斜**後，球最終漂回中心點，而非停在傾斜對應的位置。
- **現象 B（平放回彈）**：平放模式移動後停手，球以明顯的速度彈回中心，手感突兀。
- **預期行為**：手傾斜到某個方向時，球應鎖定在對應偏移位置；停手後球應緩慢滑回，而非快速彈回。
- **差異**：「持續傾斜時球回中心」與「停手後球快速彈回」均為意外行為，嚴重影響使用者對平放模式的操控感。

---

## 根因分析

1. **重力補償 (enableFlatGravityCorrection) 在傾斜時仍活躍** → `idleRatio` 計算使用 `halfDz × 2f` 作為分母，當訊號已超過死區（球正在移動）時，idleRatio 仍有 40~75% 的值，重力補償持續把 `calibratedAcceleration` (tare) 拉向 `filteredAcceleration`，相當於把當前傾斜位置學習為新的中心，debiased 趨近 0 → 球被「吸回中心」。**結果：確認（主因）**

   - 數值驗算（平放 dz.x = 0.233）：
     - 訊號 = 0.5（已超過死區）→ idleRatio = `1 - 0.5/0.934 = 0.465` → 補償仍 46.5% 活躍 ✗
     - 修正後（normX = 0.5/0.233 = 2.15）→ idleRatio = `1 - clamp(2.15) = 0` → 補償完全停止 ✓

2. **平放 Z 軸 `idleReturnStrength = 1f`** → Z 訊號一落入死區（dead zone = 0.700），EMA 立刻以完整速度（flatAlpha × 1.0 ≈ 11%/幀）追蹤 target = 0，造成明顯的「彈回」視覺感受。**結果：確認（次因）**

3. **Log 驗證**：`最大 idleRatio: 0.933  ⚠ 重力補償曾活躍 → Tare 被修改` 與 `enableFlatGravityCorrection = true` 相互印證。**結果：確認**

**根本原因**
> `idleRatio` 以雙倍死區 (`halfDz × 2f`) 為門檻，導致訊號早已超過死區（球在移動）時重力補償仍大幅活躍，讓 tare 緩慢追蹤傾斜角度，使偏移被自動歸零。

---

## 修復方案

### 概念說明

- **修改 1**：idleRatio 改為逐軸獨立正規化並取最大值。只要任一軸的 debiased 超過其自身死區，idleRatio 立即為 0，重力補償完全停止。真正靜止時（兩軸皆在死區內）才允許補償運行。
- **修改 2**：平放 Z 軸 `idleReturnStrength` 從 `1f` 降至 `0.3f`，減少訊號靜止後的回彈速度（半衰期從 ≈6 幀延長至 ≈21 幀）。

### 重要程式碼變更

**修改 1：idleRatio 計算**（`AccelerometerBallEffect.cs:484-486`）

```csharp
// 修改前（問題：超過死區後補償仍 40~75% 活躍）
float halfDz    = (s.axisDeadzone.x + s.axisDeadzone.z) * 0.5f;
float idleRatio = 1f - Mathf.Clamp01(
    (Mathf.Abs(debiased.x) + Mathf.Abs(debiased.z)) / Mathf.Max(halfDz * 2f, 0.01f));
```

```csharp
// 修改後（任一軸超過其死區 → idleRatio = 0 → 補償立即停止）
float normX     = Mathf.Abs(debiased.x) / Mathf.Max(s.axisDeadzone.x, 0.01f);
float normZ     = Mathf.Abs(debiased.z) / Mathf.Max(s.axisDeadzone.z, 0.01f);
float idleRatio = 1f - Mathf.Clamp01(Mathf.Max(normX, normZ));
```

**修改 2：flatSettings 預設值**（`AccelerometerBallEffect.cs:112`）

```csharp
// 修改前
idleReturnStrength = new Vector3(0.02f, 1f, 1f)
// 修改後
idleReturnStrength = new Vector3(0.02f, 1f, 0.3f)
```

> ⚠️ 若 Inspector 中已有覆寫值，需手動將「平放模式 → Idle Return Strength Z」從 `1` 改為 `0.3`。

---

## 驗證清單
- [ ] 手持續傾向右側時，球穩定停在右側（不再被吸回中心）
- [ ] 停手後球緩慢滑回中心（無明顯彈跳感）
- [ ] 真正靜止放置手機一段時間後，四元數漂移仍能被緩慢補償（重力補償未完全失效）

---

## 影響範圍

| 面向 | 說明 |
| ---- | ---- |
| **影響範圍** | 平放模式的重力補償邏輯（`enableFlatGravityCorrection = true` 時生效）；`flatSettings.idleReturnStrength.z` 的歸中行為 |
| **潛在風險** | 修改 1 讓重力補償更保守，長時間使用若有四元數漂移，補償效果變慢；修改 2 若 Z 訊號長期偏移（感測器異常），球回中心的速度變慢 |

---

## Lessons Learned

1. **成功做對**：透過 log 的 `idleRatio` 數值（0.933）與 `⚠ Tare 被修改` 警告直接定位到重力補償是主因，避免在其他地方浪費時間。
2. **改進空間**：`halfDz × 2f` 的原始設計意圖是讓 idleRatio 在接近死區邊緣時平滑過渡，但沒有考慮「訊號超過死區時補償仍活躍」的問題。應改為逐軸獨立判斷從設計之初就確保正確性。
3. **預防措施**：對於「重力補償」類的 tare 自動修改功能，應在設計時明確定義「何時補償、何時完全停止」的邊界，並在 log 中同時顯示 normX/normZ 值，方便日後排查。

---

## 後續待辦
- [ ] 在 Unity 實機測試：確認 X 軸持續傾斜時球不再被吸回中心
- [ ] Inspector 手動調整平放 `Idle Return Strength Z` 為 `0.3`（若有 Inspector 覆寫值）
- [ ] 觀察長時間使用後四元數漂移補償效果是否仍足夠（修改 1 讓補償更保守）

---

## 標籤
`#平放模式` `#重力補償` `#idleRatio` `#AccelerometerBallEffect` `#Tare漂移` `#回彈修復`

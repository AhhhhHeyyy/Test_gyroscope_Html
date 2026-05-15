# 工作紀錄

## 基本資訊
| 欄位 | 內容 |
|------|------|
| **日期** | 2026-05-16 |
| **問題類型** | 診斷 + 程式修正 |
| **嚴重程度** | Major |
| **狀態** | 已修正（per-axis blend + 嚮導 Tare 時機） |
| **相關腳本** | `AccelerometerBallEffect.cs` |
| **Issue/Ticket** | N/A |

---

## 問題描述

### 原始症狀
- 平放模式球的**方向與速度亂跑**（不是不動，是隨機方向移動）
- 嚮導校正完成後，靜置時球仍持續偏移到固定方向，不在中心
- 移動感不明顯、反應遲鈍

---

## 根因分析

### Root Cause Analysis

#### 問題 1：`flatLinearBlend` 單一值同時污染 X 與 Z 軸
- **症狀**：X 軸訊號在使用者移動時快速正負跳換（pre.x 從 −3.16 → +0.53 → −1.61）
- **根因**：`rawAcc.x = gDevice.x + linX × 0.4`，linX 是線性加速度（手部任何移動都有），混入後 X 軸跟著手部晃動亂跳
- **修正**：拆成 `flatLinearBlendX = 0`（X 純傾斜）+ `flatLinearBlendZ = 0.5`（Z 補入限幅 linZ）+ `flatLinZClamp = 8f`

#### 問題 2：嚮導 Tare 記錄在錯誤時機點
- **症狀**：嚮導完成後 0.5s 以內 pre ≈ 0（正確），但 1.5s 後 pre.z 持續在 +0.7～+1.0，靜置時球被固定推向 −Z
- **根因**：`ApplyWizardResults()` 呼叫 `Recalibrate()`，Recalibrate 使用**按下「套用」那一刻**的 `filteredAcceleration` 作為 Tare。Forward 步驟剛結束時手機不在自然靜止角，Tare 與自然持機角差了 ~1.0 m/s²
- **從 log 量出的偏差**：
  - 嚮導結束時 raw.z = −1.76（Tare 被記為 −1.742）
  - 使用者自然靜置時 raw.z ≈ −0.74（差 1.0 m/s²，遠超死區 0.17）
- **修正**：改用 `wizardFlatBaseline`（Baseline 靜止採樣均值）作為 Tare，兩個模式的 `savedTare` 同時寫入

#### 問題 3：重力補償在 Tare 偏移時永遠不啟動（次要，已知問題）
- `idleRatio = 1 - clamp01(|deb| / halfDz×2)`，當 debiased > deadzone 時 idleRatio = 0
- Tare 偏移 1.0 m/s² 時 idleRatio = 0 → 修正永遠不觸發 → 死循環
- **此次未修正**，根本解是修正 A（讓 Tare 一開始就正確）；若要 fallback 自動修正需改寬 idleRatio 窗口

#### 問題 4：雙重平滑造成遲鈍（同步改善）
- `inputFilterTime = 0.12s` + `smoothSpeed = 7` EMA 雙層加起來延遲 ~0.25s
- **修正**：`flatSettings.inputFilterTime` 預設從 `0.12` 降至 `0.06`

---

## 解決方案 / 實作內容

### 程式碼修改（已套用）

| 修改 | 位置 | 原值 | 新值 |
|------|------|------|------|
| 單一 blend 拆成 per-axis | [L104–109](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L104) | `flatLinearBlend = 0.4` | `flatLinearBlendX=0, flatLinearBlendZ=0.5, flatLinZClamp=8` |
| HandleGyroscopeData blend 計算 | [L650–654](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L650) | `rawAcc.x/z = grav + lin × blend` | per-axis + linZ clamp |
| HandleAcceleration blend 計算 | [L703–708](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L703) | 同上 | 同上 |
| flatSettings sensitivity 預設 | [L109](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L109) | `(0.08, 0.08, 0.08)` | `(0.3, 0.3, 0.3)` |
| flatSettings inputFilterTime 預設 | [L111](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L111) | `0.12f` | `0.06f` |
| pendingFlatSensitivity 預設 | [L320](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L320) | `(0.08, 0.08, 0.08)` | `(0.3, 0.3, 0.3)` |
| 嚮導 Tare 改用 Baseline 均值 | [L1493–1501](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L1493) | `Recalibrate()` 直接用當前 filteredAcc | 先將 filteredAcc 替換為 baseline，再
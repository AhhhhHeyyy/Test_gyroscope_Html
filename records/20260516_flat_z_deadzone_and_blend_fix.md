# 工作紀錄

## 基本資訊
| 欄位 | 內容 |
|------|------|
| **日期** | 2026-05-16 |
| **問題類型** | 診斷 + 參數調整 |
| **嚴重程度** | Major |
| **狀態** | 已緩解（flatLinearBlend=0.3）；程式端 per-axis 拆分待實作 |
| **相關腳本** | `AccelerometerBallEffect.cs` |
| **Issue/Ticket** | N/A |

---

## 問題描述

### 原始症狀
- 直立與平放模式的 **Z 軸位移幾乎沒有反應**，相較 X 軸明顯不對稱
- 平放模式 X 軸**容易暴衝**（位置跑到 ±20 以上）
- 平放 Y 軸完全靜止

### 背景條件
- 初始設定：平放 sensitivity 全部為 0.08（遠低於直立的 0.3）
- flatLinearBlend = 1（本次診斷前的測試值）
- UDP 四元數模式（Android GAME_ROTATION_VECTOR）

---

## 根因分析

### Root Cause Analysis

1. **Z 死區過大（主因）**
   → **原因**：嚮導 Forward 階段用「最大力道猛推」，最小衝程峰值 ≈ 6.2 m/s²
   → `refinedDz = minPeakMagnitude × 0.4 = 2.48`
   → 日常輕柔前後傾（pre.z 約 ±1~2）全被死區截掉，Z 幾乎永遠輸出 0
   → **緩解**：向導 Forward 步驟改用日常力道，或手動將 `axisDeadzone.z` 降到 0.6~1.2

2. **平放初始 sensitivity 設為 0.08**
   → 嚮導不修改 Y 軸 sensitivity，初始值直接留著
   → 結果：嚮導後 `sens.y = 0.08`，Y 軸幾乎沒有輸出
   → **修正**：初始 sensitivity 統一改為 0.3（與直立模式一致）

3. **flatLinearBlend 對 X/Z 效果相反**
   → X 軸（gDevice.x）訊號乾淨、持續：blend=0 最穩定，加入 linX 反而引入噪聲
   → Z 軸（gDevice.y）當手機平放時訊號微弱：需要 linZ 補充才能超過死區
   → 兩軸共用單一 blend 值，無法同時最佳化
   → **緩解**：blend=0.3（linZ 尖峰從 ±28 縮到 ±8）；根本解需拆成 per-axis

4. **linZ 尖峰在 blend=1 時過大**
   → blend=1 時 raw.z 達到 ±28 m/s²（正常重力僅 9.8）
   → 每次手部抖動或快速移動都讓球直接衝到 Z 上限再彈回 → 「彈動」感
   → **緩解**：降低 flatLinearBlend 到 0.3

### 最終根因
> 嚮導 Forward 階段記錄最小衝程峰值，用 40% 作為死區。
> 使用者做「最大力道」動作時，連最小峰值都很大，
> 導致死區被設為正常操作幅度的 1–2 倍，Z 軸有效輸入被完全封鎖。

---

## 解決方案 / 實作內容

### 參數調整（已套用）
| 參數 | 修改前 | 修改後 | 效果 |
|------|--------|--------|------|
| 初始 flat sensitivity | 0.08 / 0.08 / 0.08 | **0.3 / 0.3 / 0.3** | sens.y 從嚮導後 0.08 → 0.30 |
| flatLinearBlend | 1.0 | **0.3** | linZ 尖峰從 ±28 縮到 ±8 m/s² |
| 平放 maxOffsetPerAxis | 5 | **3** | 最大物理位移從 ±25 縮到 ±15 |

### 嚮導操作修正（使用者端）
- Forward 步驟應使用**日常力道輕柔前後傾**，包含小幅、中幅、大幅各幾次
- 目標：最小衝程峰值約 1.5~3 m/s² → 死區約 0.6~1.2
- 不要只做「最大力道猛推」——向導取最小值當死區基準，不是最大值

### 程式端待實作（per-axis blend）

**修改位置**：[AccelerometerBallEffect.cs:104](../UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs#L104) 和 L552、L604

```csharp
// 現行（單一 blend）
[SerializeField] [Range(0f, 5f)] private float flatLinearBlend = 0.3f;
rawAcceleration.x = _gravX + _linX * flatLinearBlend;
rawAcceleration.z = _gravZ + _linZ * flatLinearBlend;

// 待改（per-axis + clamp）
[SerializeField] [Range(0f, 5f)] private float flatLinearBlendX = 0f;
[SerializeField] [Range(0f, 5f)] private float flatLinearBlendZ = 0.5f;

float clampedLinZ = Mathf.Clamp(_linZ, -8f, 8f);  // 截掉極端尖峰
rawAcceleration.x = _gravX + _linX * flatLinearBlendX;
rawAcceleration.z = _gravZ + clampedLinZ * flatLinearBlendZ;
```

---

## 測試數據對照

### 嚮導結果比較（三次校正）

| | 第一次（全力猛推）| 第二次（快慢混合）| 說明 |
|--|-----------------|-----------------|------|
| 直立 dz.z | 2.55 | 0.79 | 動作力道影響極大 |
| 平放 dz.z | 2.48 | 1.06 | |
| 平放 Z 最大位移 | 幾乎 0 | 5.00（滿偏） | 死區縮小後 Z 正常響應 |
| 位置抖動（最大） | 8.94m | 5.63m → 3.80m | maxOffset 縮小後改善 |

### FlatPipe 關鍵數據（blend=0.3 後）
```
感覺順多了（使用者回報）
linZ 尖峰由 ±28 m/s² 降至 ±8 m/s²
Z 軸不再頻繁衝到邊界再彈回
```

---

## 驗證清單
- [x] 確認 Z 軸有響應（最大 targetOffset Z 達到 5.000）
- [x] flatLinearBlend 降到 0.3 後「彈動」明顯改善
- [x] 初始 sensitivity 改為 0.3，sens.y 從嚮導後獲得正常值
- [ ] 實作 per-axis flatLinearBlend（flatLinearBlendX / flatLinearBlendZ）
- [ ] 實作 linZ 上限 clamp（建議 ±8 m/s²）
- [ ] 嚮導說明文字修改：Forward 步驟明確告知「用日常力道，輕重各幾次」
- [ ] 重新跑嚮導驗證修正後的說明是否讓參數正常

---

## 影響範圍與相容性
| 面向 | 說明 |
|------|------|
| **影響範圍** | 平放模式 Z 軸輸入、flatLinearBlend、嚮導 Forward 死區計算 |
| **向下相容性** | 舊有嚮導結果若 dz.z > 1.5，Z 軸仍會遲鈍，需重新跑嚮導 |
| **潛在副作用** | per-axis blend 需同步更新 Inspector 標籤和 Tooltip |

---

## 經驗教訓 Lessons Learned
1. **嚮導 Forward 的意義誤解**：使用者以為是「最大力道」，實際上應代表「日常操作範圍」；說明文字需更明確
2. **X/Z 訊號特性不同**：平放時 gDevice.x（左右）強且穩定，gDevice.y（前後）微弱且噪聲高，兩者不應套同一個 blend 值
3. **初始參數影響嚮導後結果**：嚮導不修改 Y 軸 sensitivity，初始值 0.08 會直接保留到校正後

---

## 後續待辦
- [ ] 實作 per-axis blend + linZ clamp
- [ ] 修改嚮導 Forward 步驟的 UI 說明文字
- [ ] 考慮在嚮導 Baseline 階段加入 linZ 的噪聲評估（目前只評估重力投影）

---

## 標籤
`#平放模式` `#Z軸死區` `#flatLinearBlend` `#嚮導校正` `#per-axis` `#linZ尖峰` `#AccelerometerBallEffect`

# 分析紀錄：平放模式上下映射不明顯

## 基本資訊

| 欄位 | 內容 |
|------|------|
| **日期** | 2026-05-08 |
| **問題類型** | 行為分析 / 設計說明 |
| **嚴重程度** | 設計限制（非 Bug） |
| **狀態** | 已分析，提供修改方向 |
| **受影響模組** | `web-demo/gyro-demo.html`、`AccelerometerBallEffect.cs` |

---

## 問題描述

`gyro-demo.html` 在平放模式（`phoneIsFlat = true`）下，球的上下移動（Three.js Y 軸 / 球的高度）幾乎沒有反應，與直立模式的上下映射相比明顯不足。

---

## 根本原因分析

### 原因一：Y 軸遮罩直接歸零（最根本）

```js
// gyro-demo.html FLAT 設定
const FLAT = {
  movementAxesMask: new THREE.Vector3(1, 0, 1),  // Y = 0，球高度永遠不動
};
```

對應 C# `flatSettings.movementAxesMask = new Vector3(1, 0, 1)`，兩者一致。  
平放模式的設計本意是讓球只在 XZ 水平面移動（左右＋前後），球的高度**完全被遮蔽**。

---

### 原因二：rawAcceleration.y 的物理來源不直覺

```js
rawAcceleration.set(
  worldAcc.x + gDevice.x * GLOBAL.flatGravWt,  // X: 水平左右 + 重力傾斜
  worldAcc.z,                                   // Y: Android 世界 Z（真實世界垂直方向）
  worldAcc.y                                    // Z: Android 世界 Y（前後）
);
```

`worldAcc.z` 代表「把手機往天花板方向物理推動」的線性加速度，這個動作在正常使用下幾乎不會發生，即使 Y 軸遮罩開啟也難以激發。

---

### 原因三：gDevice.y（前後傾斜重力分量）完全未接入

```js
// 只有 X 軸有重力傾斜補償，Y/Z 軸沒有
rawAcceleration.x = worldAcc.x + gDevice.x * GLOBAL.flatGravWt;
// gDevice.y ← 手機前後傾斜（Pitch）的重力分量，完全未使用
```

當使用者把平放的手機往前傾（Pitch），`gDevice.y` 會有明顯分量，但在 C# 和 HTML 中均未被引用。這使得「傾斜手機 → 球上下移動」的互動方式完全失效。

---

### 原因四（視覺面）：Z 軸移動在畫面上不是純粹的上下

攝影機位於 `(0, 4, 10)` 斜向俯視，Three.js Z 軸的球移動呈現為透視縮放感（往遠方縮小／往近方放大），視覺上遠不如 X 軸左右移動直觀。

---

## 原因彙整

| 原因 | 影響 |
|------|------|
| `movementAxesMask.y = 0` | 球高度完全被關閉，Y 通道輸入無效 |
| `rawAcceleration.y = worldAcc.z` | 對應「垂直推動手機」，使用上不自然 |
| `gDevice.y` 未接入 | 前後傾斜（Pitch）重力分量沒有驅動任何軸 |
| 攝影機斜角 | Z 軸移動呈深度透視感，不像上下 |

---

## 建議修改方向

若想讓「前後傾斜平放手機」能明顯控制球的某個方向，最直接的做法是把 `gDevice.y` 的重力貢獻疊加到 Z 通道，對稱於目前 X 軸的處理方式：

```js
// 修改前
rawAcceleration.set(
  worldAcc.x + gDevice.x * GLOBAL.flatGravWt,
  worldAcc.z,
  worldAcc.y
);

// 修改後（讓前後傾斜也有重力驅動）
rawAcceleration.set(
  worldAcc.x + gDevice.x * GLOBAL.flatGravWt,
  worldAcc.z,
  worldAcc.y + gDevice.y * GLOBAL.flatGravWt  // ← 加入前後傾斜重力分量
);
```

同步需要把 `FLAT.movementAxesMask.z` 確保為 `1`（目前已是），並視實際效果調整 `sensitivity` 與 `axisDeadzone.z`。

---

## 相關檔案

| 檔案 | 關聯 |
|------|------|
| `web-demo/gyro-demo.html` | 主要分析對象，handlePacket() 第 549-566 行、FLAT 第 348-357 行 |
| `UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs` | C# 原始實作，HandleAcceleration() 第 277-300 行 |
| `records/20260413_mode_switch_fix.md` | 模式切換修復紀錄，背景參考 |

---

## 標籤

`#平放模式` `#上下映射` `#感測器融合` `#gDevice` `#movementAxesMask` `#gyro-demo`

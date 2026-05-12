# 缺陷修復紀錄

## 基本資訊

| 欄位 | 內容 |
|------|------|
| **日期** | 2026-05-08 |
| **問題類型** | 缺陷修復 / 感測器輸入映射錯誤 |
| **嚴重程度** | Major（直立模式 X 軸完全無法持位） |
| **狀態** | 已修復 |
| **受影響模組／檔案** | `web-demo/gyro-demo.html`（`handlePacket` 函式） |
| **關聯 Issue/Ticket** | N/A |

---

## 問題描述／需求背景

使用者在 `gyro-demo.html` 操作時，傾斜手機移動球體後，球體在手機停止移動的瞬間即快速彈回畫面原點（`(0,0,0)`），無法停在傾斜角對應的位置。

- **預期行為**：手機向左傾 → 球體向左移動並停留
- **實際行為**：手機向左傾 → 球體短暫向左移動 → 立即彈回原點

對比同專案的 `AccelerometerBallEffect.cs`（Unity），Y 軸（前後傾仰）不會彈回，但 X 軸（左右傾斜）同樣彈回，兩者行為不一致，且與 `AccelerometerBallEffect.md` 文件所述「以手機傾斜角（重力方向）控制物件在 XY 平面的位移」相矛盾。

---

## 根本原因分析（Root Cause Analysis）

### 問題拆解

**直立模式三軸的輸入來源對比：**

| 軸 | 修復前輸入來源 | 物理性質 | 手機靜止時值 | 結果 |
|----|---------------|----------|-------------|------|
| Y | `gDevice.z`（重力在裝置 Z 軸的投影） | **持續量**（傾斜角不變值不變） | ≈ 傾斜角對應值 | ✓ 停留 |
| X | `worldAcc.x`（TYPE_LINEAR_ACCELERATION） | **瞬時量**（只有加速時非零） | **≈ 0** | ✗ 彈回 |
| Z | `worldAcc.y`（TYPE_LINEAR_ACCELERATION） | **瞬時量** | **≈ 0** | ✗ 彈回 |

**問題一：X 軸使用線性加速度（gyro-demo.html 第 560 行，修復前）**

- `rawAcceleration.x = worldAcc.x`
- `TYPE_LINEAR_ACCELERATION` 是已去除重力的慣性加速度，僅在手機加速或減速瞬間非零
- 手機傾斜後靜止保持角度 → 線性加速度立即降至 ≈ 0 → `debiased.x → 0` → `targetOffset.x → 0` → SmoothDamp 把 `currentOffset.x` 拉回 0 → 球彈回

**問題二：Y 軸行為正確，X 軸與 Y 軸不一致**

- Y 軸使用 `gDevice.z`（重力在裝置 Z 軸的投影），代表螢幕朝向地面的分量
- 這是「傾斜角」量：手機保持傾斜，值不變 → 球停留
- X 軸應對稱地使用 `gDevice.x`（重力在裝置 X 軸的投影 = roll 角）
- 文件 `AccelerometerBallEffect.md` 明確寫明「以傾斜角（重力方向）控制 XY 平面位移」，但程式碼使用線性加速度，形成文件與實作的差異

### 最根本原因

> 直立模式 X 軸的輸入來源為「線性加速度（瞬時量）」，而非「重力投影（持續量）」，導致手機靜止時 X 軸輸入歸零，球體彈回原點。

---

## 解決方案／修改內容

### 方案概覽

將直立模式 X 軸的輸入來源從 `worldAcc.x`（線性加速度）改為 `gDevice.x`（重力在裝置 X 軸的投影），與 Y 軸（`gDevice.z`）保持一致，實現「傾斜角 → 持續位移」的控制方式。

Z 軸保留 `worldAcc.y`（線性加速度），提供「推力感」的深度控制。

### 關鍵程式碼變更

**修改位置**：`web-demo/gyro-demo.html`，`handlePacket` 函式

**修改前（第 542–566 行）：**
```javascript
if (!phoneIsFlat) {
    // 直立模式：Y 軸 = 重力 Z 分量（前後傾斜角）
    rawAcceleration.y = gDevice.z;
}

const acc = new THREE.Vector3(ax, ay, az);
if (hasOrientationData && phoneIsFlat) {
    // ... 平放模式 ...
} else if (hasOrientationData && !phoneIsFlat) {
    const worldAcc = acc.clone().applyQuaternion(orientation);
    rawAcceleration.x = worldAcc.x; // ← 線性加速度（問題所在）
    rawAcceleration.z = worldAcc.y;
}
```

**修改後：**
```javascript
if (!phoneIsFlat) {
    // 直立模式：用重力投影取代線性加速度，使球停在傾斜角對應位置而不彈回
    // X = gDevice.x → 左右傾斜（roll）持續量；Y = gDevice.z → 前後傾斜（pitch）持續量
    rawAcceleration.x = gDevice.x;  // ← 改為重力投影（持續量）
    rawAcceleration.y = gDevice.z;
}

const acc = new THREE.Vector3(ax, ay, az);
if (hasOrientationData && phoneIsFlat) {
    // ... 平放模式（不變）...
} else if (hasOrientationData && !phoneIsFlat) {
    const worldAcc = acc.clone().applyQuaternion(orientation);
    // Z 軸仍用線性加速度（前後推力，深度感）；X/Y 已由 gDevice 設定
    rawAcceleration.z = worldAcc.y;
}
```

### 各軸修復後行為

| 軸 | 輸入來源（修復後） | 手機靜止時 | 效果 |
|----|-------------------|-----------|------|
| X | `gDevice.x`（roll 傾斜角） | 維持傾斜值 | 向左傾 → 球左移並**停留** |
| Y | `gDevice.z`（pitch 傾斜角） | 維持傾斜值 | 向前傾 → 球前移並**停留**（原本即正確） |
| Z | `worldAcc.y`（線性加速度） | ≈ 0 | 推手機 → 球移動後彈回（保留推力感） |

---

## 驗證清單

- [ ] 直立模式向左傾斜：球向左移動並停留（不彈回）
- [ ] 直立模式向右傾斜：球向右移動並停留
- [ ] 直立模式向前後傾仰：球上下移動並停留（原本即正確，確認未受影響）
- [ ] 直立模式靜止放平：球回到原點（`gDevice.x ≈ 0`，校正後應歸零）
- [ ] 平放模式行為：確認未受此次修改影響
- [ ] 按校正按鈕：傾斜位置正確設為新原點

---

## 影響評估

| 面向 | 說明 |
|------|------|
| **影響範圍** | 僅 `gyro-demo.html` 的 `handlePacket` 函式，約 3 行 |
| **向下相容性** | 無 API 變更，不影響 WebSocket 封包格式 |
| **潛在副作用** | 直立模式控制方式從「推力模型」變更為「傾斜模型」；Z 軸仍為推力，X/Y 為傾斜，存在混合感 |
| **效能影響** | 無（`gDevice` 每幀已計算，僅改賦值目標） |
| **未解問題** | 平放模式所有軸仍為線性加速度，仍會彈回；如需改善需另外處理 `gDevice.x/y` 在平放姿態下的映射 |

---

## 時間軸

| 時間 | 事件 |
|------|------|
| 2026-05-08 | 問題確認：`gyro-demo.html` 直立模式球體彈回原點 |
| 2026-05-08 | 對比 `AccelerometerBallEffect.cs`，定位輸入來源差異 |
| 2026-05-08 | 確認根本原因：`rawAcceleration.x = worldAcc.x` 為線性加速度（瞬時量） |
| 2026-05-08 | 修復：改為 `rawAcceleration.x = gDevice.x`（重力投影，持續量） |

---

## 經驗教訓（Lessons Learned）

1. **輸入量的時間特性決定控制感**：重力投影（角度量）是持續量；線性加速度是瞬時量。混用兩種量在同一控制迴路中會造成各軸行為不一致，難以預期
2. **文件與程式碼一致性**：`AccelerometerBallEffect.md` 明確說明「以傾斜角控制 XY」，但程式碼使用線性加速度，應於修復時同步對齊文件描述
3. **對比已知正確軸找 Bug**：Y 軸行為正確（`gDevice.z`），以此為基準對比 X 軸，迅速定位問題

---

## 後續任務

- [ ] 實機測試上述六項驗證清單
- [ ] 評估 Z 軸是否也應改為 `gDevice.y`（前後俯仰投影）以統一控制模型
- [ ] 研究平放模式 X/Z 彈回的改善方案（考慮使用平放時的 gDevice.x/y 微小分量）
- [ ] 將此次 `gDevice` 用法同步更新至 `AccelerometerBallEffect.cs`（確認 C# 版是否有相同問題）

---

## 標籤

`#感測器融合` `#重力投影` `#線性加速度` `#直立模式` `#網頁版` `#gyro-demo` `#輸入映射` `#彈回原點`

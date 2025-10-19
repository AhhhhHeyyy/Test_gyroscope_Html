# Unity 搖晃狀態顯示設置指南 (v2.2)

## 📋 概述
本指南將幫助您在 Unity 中設置搖晃狀態的文字顯示功能，讓您能夠在 Unity 場景中即時看到來自網頁的搖晃偵測數據。

## 🆕 v2.2 更新內容
- ✅ **已整合到 GyroscopeController**：搖晃狀態直接顯示在現有調試界面
- ✅ **無需額外 UI 設置**：使用現有的 OnGUI 調試界面
- ✅ **自動事件訂閱**：GyroscopeController 自動處理搖晃事件
- ✅ **即時狀態更新**：搖晃時即時顯示狀態、次數、強度、類型

## 🔧 已創建的腳本文件

### 1. ShakeData.cs
- **功能**：定義搖晃數據結構
- **包含**：搖晃次數、強度、類型、加速度、時間戳

### 2. GyroscopeReceiver.cs
- **功能**：WebSocket 接收器（已更新）
- **新增**：搖晃數據接收和事件觸發

### 3. GyroscopeController.cs
- **功能**：陀螺儀控制 + 搖晃狀態顯示（已整合）
- **特色**：在現有的調試界面中顯示搖晃狀態

## 🎮 Unity 場景設置步驟 (v2.2 簡化版)

### 步驟 1：確認腳本文件
確保以下腳本文件存在且正確：
- ✅ `ShakeData.cs` - 搖晃數據結構
- ✅ `GyroscopeReceiver.cs` - 已更新支援搖晃事件
- ✅ `GyroscopeController.cs` - 已整合搖晃狀態顯示

### 步驟 2：設置 GyroscopeController (唯一需要設置的步驟)
1. 在場景中找到或創建一個 GameObject
2. 添加 `GyroscopeController.cs` 腳本
3. 在 Inspector 中設置：
   - **Gyro Receiver**：拖拽 GyroscopeReceiver GameObject
   - **Show Debug Info**：✅ 勾選（重要！）

### 步驟 3：設置 GyroscopeReceiver
1. 確保場景中有 GyroscopeReceiver GameObject
2. 檢查 GyroscopeReceiver 腳本是否為更新版本
3. 確認 WebSocket URL 設置正確

**🎉 完成！** 搖晃狀態會自動顯示在 GyroscopeController 的調試界面中。

## 🎯 功能特色

### 即時顯示（在左上角調試界面）
- **陀螺儀數據**：目標旋轉、當前旋轉、平滑旋轉、位置
- **搖晃狀態**：靜止、搖晃中、強烈搖晃、劇烈搖晃
- **搖晃次數**：累計搖晃次數
- **搖晃強度**：當前搖晃的加速度強度 (m/s²)
- **搖晃類型**：一般、強烈、劇烈

### 視覺效果
- **即時更新**：搖晃時文字會即時更新
- **自動重置**：2秒後自動恢復為靜止狀態
- **調試界面**：在 Unity 遊戲視窗左上角顯示

### 調試功能
- **Console 輸出**：詳細的搖晃偵測日誌
- **Inspector 顯示**：即時狀態查看
- **錯誤處理**：完整的錯誤捕獲和顯示

## 🎨 顯示布局

### 調試界面布局
```
┌─────────────────┐ ← 陀螺儀數據區域 (10, 220)
│ 目標旋轉: ...   │
│ 當前旋轉: ...   │
│ 平滑旋轉: ...   │
│ 位置: ...       │
│ [重置旋轉]      │
│ [重置位置]      │
└─────────────────┘

┌─────────────────┐ ← 搖晃狀態區域 (10, 380)
│ 搖晃狀態: 靜止  │
│ 搖晃次數: 0     │
│ 搖晃強度: 0.00  │
│ 搖晃類型: 一般  │
└─────────────────┘
```

## 🚀 測試步驟

1. **啟動 Unity 場景**
2. **確認調試模式**：在 GyroscopeController 中勾選 "Show Debug Info"
3. **開啟網頁**：在手機上開啟 `index.html`
4. **允許權限**：允許瀏覽器存取感測器
5. **搖晃手機**：觀察 Unity 左上角的搖晃狀態變化
6. **檢查 Console**：查看詳細的偵測日誌

## 🔍 故障排除

### 常見問題
1. **看不到搖晃狀態**：確認 "Show Debug Info" 已勾選
2. **文字不更新**：檢查 GyroscopeReceiver 連接狀態
3. **沒有搖晃事件**：確認 WebSocket 連接正常
4. **Console 錯誤**：查看 GyroscopeReceiver 連接狀態

### 調試技巧
- 開啟 Console 查看詳細日誌
- 檢查 Inspector 中的連接狀態
- 確認網頁和 Unity 使用相同的 WebSocket URL
- 測試時使用較大的搖晃動作

## 📱 完整工作流程

1. **網頁端**：手機搖晃 → 加速度感測器 → 搖晃偵測 → WebSocket 發送
2. **Unity 端**：WebSocket 接收 → 解析數據 → 觸發事件 → 調試界面更新
3. **用戶體驗**：在 Unity 左上角即時看到搖晃狀態的文字顯示

## 🎉 完成！

現在您只需要：
1. ✅ **確認腳本文件**：ShakeData.cs, GyroscopeReceiver.cs, GyroscopeController.cs
2. ✅ **設置 GyroscopeController**：勾選 "Show Debug Info"
3. ✅ **啟動測試**：搖晃手機，觀察左上角調試界面

搖晃狀態會直接顯示在現有的陀螺儀調試界面中，無需額外的 UI 設置！🎯
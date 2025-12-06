# AR.js 位移追踪系统使用说明

## 📋 目录
1. [系统概述](#系统概述)
2. [功能特点](#功能特点)
3. [使用前准备](#使用前准备)
4. [使用步骤](#使用步骤)
5. [技术参数](#技术参数)
6. [常见问题](#常见问题)
7. [故障排除](#故障排除)

---

## 系统概述

本系统实现了通过手机摄像头追踪 Marker 图案，实时获取手机在真实世界中的位置和旋转信息，并通过 WebSocket 传输到 Unity 应用程序，实现手机移动控制 Unity 中 3D 物体的位置追踪。

### 系统架构

```
手机浏览器 (iOS/Android)
    ↓
AR.js Marker 追踪
    ↓
WebSocket 数据传输
    ↓
Unity 应用程序 (Windows/Mac)
    ↓
3D 物体位置更新
```

---

## 功能特点

### ✅ 主要功能

1. **基于 Marker 的 6DOF 追踪**
   - 使用 AR.js 开源库
   - 追踪相机相对于 Marker 的位置（X, Y, Z）
   - 追踪相机相对于 Marker 的旋转（四元数）

2. **实时数据传输**
   - WebSocket 实时通信
   - 60fps 位置更新
   - 低延迟数据传输

3. **可视化数据面板**
   - 实时显示 Position (X, Y, Z)
   - 实时显示 Rotation (X, Y, Z, W)
   - 实时显示 Delta（相对位移）
   - FPS 帧率显示

4. **可调参数**
   - 位置缩放比例（Scale）
   - 位置偏移量（Offset X, Y, Z）
   - 重置初始位置

### 🎯 优势

- ✅ **完全免费**：无需 API Key，开源库
- ✅ **稳定可靠**：基于标记追踪，精度高
- ✅ **跨平台**：支持 iOS 和 Android
- ✅ **易于使用**：打印 Marker 即可使用
- ✅ **实时同步**：Unity 物体实时跟随手机移动

---

## 使用前准备

### 1. 准备 Marker 图案

#### 方法一：使用默认 Hiro Marker（推荐）

1. 访问 AR.js 官网下载 Hiro Marker：
   - 网址：https://jeromeetienne.github.io/AR.js/data/images/HIRO.jpg
   - 或使用项目中的默认 Marker

2. 打印 Marker：
   - 使用 A4 纸张打印
   - 确保 Marker 清晰可见
   - 建议尺寸：10cm × 10cm 或更大

#### 方法二：生成自定义 Marker

1. 访问 Marker 生成器：
   - https://jeromeetienne.github.io/AR.js/three.js/examples/marker-training/examples/generator.html

2. 上传图片或使用默认图案生成 `.patt` 文件

3. 下载并替换 `position-test.html` 中的 Marker URL

### 2. 环境要求

- **网络环境**：需要 HTTPS 或 localhost（AR.js 要求）
- **浏览器**：支持 WebRTC 的现代浏览器
  - Chrome/Edge（Android）
  - Safari（iOS 11+）
- **设备**：带摄像头的手机（iOS/Android）
- **Unity 端**：运行 Unity 应用程序并连接到 WebSocket 服务器

### 3. 服务器配置

确保 WebSocket 服务器正在运行：
- 默认地址：`wss://testgyroscopehtml-production.up.railway.app`
- 如需修改，编辑 `position-test.html` 中的 WebSocket URL

---

## 使用步骤

### 第一步：打开位移测试页面

1. 在手机浏览器中打开主页面：`index.html`
2. 点击顶部导航栏的 **"📍 位移测试"** 按钮
3. 页面会自动跳转到 `position-test.html`

### 第二步：允许摄像头权限

1. 浏览器会弹出摄像头权限请求
2. 点击 **"允许"** 或 **"允许访问摄像头"**
3. 如果拒绝，AR.js 无法启动，需要刷新页面重新授权

### 第三步：对准 Marker

1. 将打印好的 Marker 图案放在桌面上
2. 确保 Marker 在摄像头视野内
3. 保持适当距离（建议 20-50cm）
4. 确保光照充足，Marker 清晰可见

### 第四步：开始追踪

1. 当 Marker 被检测到时：
   - 状态栏会显示 "Marker: 已检测"
   - 页面中央的提示会消失
   - 数据面板开始显示实时数值

2. 移动手机：
   - **向上移动** → Position Y 增加 → Unity 物体向上
   - **向右移动** → Position X 增加 → Unity 物体向右
   - **向前移动** → Position Z 增加 → Unity 物体向前
   - **旋转手机** → Rotation 改变 → Unity 物体旋转

### 第五步：调整参数（可选）

在数据面板底部的控制区域：

1. **重置初始位置**：
   - 点击 "重置初始位置" 按钮
   - 当前 Marker 位置会被设为新的参考点

2. **调整缩放**：
   - 修改 "缩放" 输入框的数值
   - 默认 1.0，增大数值会放大移动幅度

3. **设置偏移**：
   - 修改 "偏移 X/Y/Z" 输入框
   - 用于微调 Unity 中的物体位置

---

## 技术参数

### AR.js 配置

- **追踪模式**：Pattern Marker（图案标记）
- **默认 Marker**：Hiro Marker
- **检测模式**：mono_and_matrix
- **矩阵类型**：3x3
- **最大检测率**：60fps
- **平滑追踪**：启用

### 数据格式

发送到 Unity 的数据格式：

```json
{
  "type": "position",
  "data": {
    "position": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0
    },
    "rotation": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0,
      "w": 1.0
    },
    "delta": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0
    },
    "timestamp": 1234567890
  }
}
```

### 坐标系说明

- **Position (X, Y, Z)**：相机相对于 Marker 的位置（米）
  - X：左右（右为正）
  - Y：上下（上为正）
  - Z：前后（前为正）

- **Rotation (X, Y, Z, W)**：相机相对于 Marker 的旋转（四元数）
  - Unity 兼容格式

- **Delta (X, Y, Z)**：相对于初始位置的位移（米）

---

## 常见问题

### Q1: Marker 无法被检测到？

**可能原因：**
- Marker 不在摄像头视野内
- 光照不足
- Marker 图案模糊或损坏
- 距离太近或太远

**解决方法：**
1. 确保 Marker 完全在摄像头视野内
2. 改善光照条件
3. 重新打印清晰的 Marker
4. 调整距离到 20-50cm

### Q2: WebSocket 连接失败？

**可能原因：**
- 网络连接问题
- 服务器未运行
- WebSocket URL 配置错误

**解决方法：**
1. 检查网络连接
2. 确认服务器正在运行
3. 检查 `position-test.html` 中的 WebSocket URL
4. 点击 "重新连接 WebSocket" 按钮

### Q3: 摄像头权限被拒绝？

**解决方法：**
1. 刷新页面
2. 在浏览器设置中允许摄像头权限
3. iOS Safari：设置 → Safari → 摄像头 → 允许

### Q4: Unity 物体不移动？

**可能原因：**
- WebSocket 未连接
- Unity 端未正确接收数据
- PositionController 未正确配置

**解决方法：**
1. 检查 WebSocket 连接状态（页面顶部状态栏）
2. 检查 Unity 控制台是否有错误
3. 确认 `PositionController` 已附加到目标物体
4. 确认 `GyroscopeReceiver` 正在运行

### Q5: 位置数据不准确？

**解决方法：**
1. 确保 Marker 平整放置
2. 避免 Marker 反光
3. 保持稳定的光照
4. 调整缩放比例以适应 Unity 场景

---

## 故障排除

### 问题：AR.js 初始化失败

**症状：** 状态栏显示 "AR.js: 错误" 或页面无响应

**解决步骤：**
1. 检查浏览器控制台错误信息
2. 确认 A-Frame 和 AR.js 库已正确加载
3. 检查网络连接（需要加载 CDN 资源）
4. 尝试刷新页面

### 问题：Marker 检测不稳定

**症状：** Marker 频繁丢失，追踪中断

**解决方法：**
1. 改善光照条件（避免强光和阴影）
2. 确保 Marker 平整，无褶皱
3. 保持手机稳定，避免快速移动
4. 调整 Marker 大小（建议 10cm × 10cm 或更大）

### 问题：数据更新延迟

**症状：** Unity 物体移动有延迟

**解决方法：**
1. 检查网络延迟（查看 WebSocket 状态）
2. 检查 Unity 端的平滑设置（`smoothingFactor`）
3. 降低 Unity 端的平滑系数以获得更快响应
4. 检查服务器性能

### 问题：位置方向相反

**症状：** 手机向右移动，Unity 物体向左移动

**解决方法：**
1. 调整缩放比例为负值（如 -1.0）
2. 或在 Unity 端调整坐标轴映射
3. 使用偏移量进行微调

---

## 高级配置

### 自定义 Marker

1. 生成自定义 Marker：
   ```
   访问：https://jeromeetienne.github.io/AR.js/three.js/examples/marker-training/examples/generator.html
   ```

2. 修改 `position-test.html`：
   ```html
   <a-marker 
       type="pattern" 
       url="path/to/your/custom-marker.patt"
       id="custom-marker">
   ```

### 调整追踪参数

在 `position-test.html` 的 `<a-scene>` 标签中修改：

```html
<a-scene 
    arjs="
        sourceType: webcam;
        sourceWidth: 1280;
        sourceHeight: 720;
        maxDetectionRate: 60;
        smooth: true;
        smoothCount: 10;
        smoothTolerance: .01;
        smoothThreshold: 5;
    ">
```

### Unity 端配置

在 `PositionController.cs` 中调整：

```csharp
[SerializeField] private float positionSensitivity = 1f;  // 位置敏感度
[SerializeField] private float smoothingFactor = 0.1f;   // 平滑系数
[SerializeField] private bool useDeltaMovement = true;    // 使用相对位移
```

---

## 性能优化建议

1. **降低帧率**：如果性能不足，可以降低 `maxDetectionRate`
2. **减少数据发送频率**：在 `updatePosition()` 中添加节流
3. **优化 Unity 端**：使用对象池、减少不必要的计算
4. **网络优化**：使用本地服务器减少延迟

---

## 技术支持

### 相关文档

- AR.js 官方文档：https://ar-js-org.github.io/AR.js-Docs/
- A-Frame 文档：https://aframe.io/docs/
- WebSocket API：https://developer.mozilla.org/en-US/docs/Web/API/WebSocket

### 调试工具

1. **浏览器控制台**：
   - 打开开发者工具（F12）
   - 查看 Console 标签页
   - 查看 Network 标签页（WebSocket 连接）

2. **Unity 控制台**：
   - 查看 Debug.Log 输出
   - 检查 `PositionController` 的调试信息

---

## 更新日志

### v1.0 (2024-12-06)
- ✅ 初始版本发布
- ✅ AR.js Marker 追踪集成
- ✅ WebSocket 数据传输
- ✅ 实时数据显示面板
- ✅ 参数调整功能

---

## 许可证

本系统使用以下开源库：
- AR.js：MIT License
- A-Frame：MIT License
- Three.js：MIT License

---

**最后更新：2024-12-06**


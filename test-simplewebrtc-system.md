# SimpleWebRTC 系统测试指南

## 系统架构

```
Web 浏览器 (simplewebrtc-sender.html)
    ↓ WebRTC 信令
Node.js 信令服务器 (server.js)
    ↓ WebRTC 信令  
Unity 客户端 (SimpleWebRTCReceiver.cs)
```

## 测试步骤

### 1. 启动信令服务器

```bash
cd C:\Users\user\Desktop\School\Project1141
node server.js
```

预期输出:
```
🚀 陀螺儀 & 螢幕捕獲 WebSocket伺服器啟動成功!
📱 靜態檔案服務: http://localhost:8080
🔌 WebSocket端點: ws://localhost:8080
```

### 2. 配置 Unity 场景

1. 打开 Unity 项目
2. 创建新场景或使用现有场景
3. 添加空 GameObject，命名为 "WebRTCController"
4. 添加 `SimpleWebRTCReceiver` 组件
5. 配置以下字段:
   - WebSocket Server Address: `ws://localhost:8080`
   - Room ID: `default-room`
   - Video Display: 创建 RawImage 并拖拽到字段
   - Status Text: 创建 Text 并拖拽到字段

### 3. 启动 Unity 客户端

1. 运行 Unity 场景
2. 在 Inspector 中点击 "Connect" 按钮
3. 观察状态文本显示 "WebSocket 已连接，等待 WebRTC 配对..."

### 4. 启动 Web 发送端

1. 打开浏览器访问: `http://localhost:8080/simplewebrtc-sender.html`
2. 点击 "连接" 按钮
3. 等待状态显示 "房间就绪，可以开始屏幕共享"
4. 点击 "开始屏幕共享" 按钮
5. 选择要共享的屏幕/窗口

### 5. 验证连接

预期结果:
- Unity 端状态显示 "WebRTC 连接已建立！"
- Unity 端 RawImage 显示 Web 端的屏幕内容
- 控制台输出显示视频传输开始

## 故障排除

### 问题 1: Unity 端显示灰屏

**可能原因:**
- WebRTC 连接未建立
- 视频轨道未正确绑定
- 编码器不兼容

**解决步骤:**
1. 检查 Unity Console 日志
2. 确认 Web 端屏幕共享已开始
3. 检查 SimpleWebRTC 组件配置

### 问题 2: WebSocket 连接失败

**可能原因:**
- 服务器未启动
- 端口被占用
- 防火墙阻止

**解决步骤:**
1. 确认 `node server.js` 正在运行
2. 检查端口 8080 是否可用
3. 尝试使用 `ws://127.0.0.1:8080`

### 问题 3: WebRTC 连接建立失败

**可能原因:**
- ICE 候选交换失败
- STUN 服务器不可达
- 网络配置问题

**解决步骤:**
1. 检查网络连接
2. 尝试不同的 STUN 服务器
3. 考虑使用 TURN 服务器

## 性能优化建议

### Unity 端
- 使用合适的视频分辨率 (1280x720)
- 启用硬件加速
- 定期清理未使用的资源

### Web 端
- 限制帧率到 30fps
- 使用 VP8 编码器
- 优化 Canvas 绘制

### 服务器端
- 监控连接数量
- 定期清理无效连接
- 使用负载均衡 (多实例时)

## 部署到云端

### Railway 部署
1. 更新 Unity 中的 WebSocket URL 为 Railway 地址
2. 确保使用 `wss://` 协议
3. 配置环境变量

### 注意事项
- 云端部署需要 TURN 服务器
- 确保 HTTPS/WSS 证书有效
- 监控服务器资源使用

## 测试检查清单

- [ ] 信令服务器启动成功
- [ ] Unity 客户端连接 WebSocket
- [ ] Web 端连接 WebSocket
- [ ] 房间配对成功
- [ ] WebRTC 连接建立
- [ ] 视频流传输正常
- [ ] 无灰屏问题
- [ ] 控制台无错误日志

## 下一步

如果测试成功:
1. 集成到现有完整系统
2. 添加陀螺仪数据传输
3. 优化显示比例 (1280x400)
4. 部署到生产环境

如果测试失败:
1. 检查日志输出
2. 逐步排查各组件
3. 参考故障排除指南
4. 考虑降级到 WebSocket 方案

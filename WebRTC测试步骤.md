# 🎯 WebRTC 三端对接测试指南

## 📋 测试环境
- **信令服务器**: Node.js WebSocket (端口 8081) ✅ 已运行
- **发送端**: HTML5 WebRTC (Screen1020.html)
- **接收端**: Unity WebRTC (TestScreenCon.cs)

## 🚀 测试步骤

### 步骤 1: 确认服务器运行状态
```bash
# 检查端口 8081 是否被占用
netstat -ano | findstr :8081
```
**预期输出**: 应该显示端口 8081 正在监听

### 步骤 2: Unity 接收端配置
1. **打开Unity项目**
2. **创建测试场景**:
   - 创建空GameObject，命名为 "WebRTCReceiver"
   - 添加 `TestScreenCon` 脚本
3. **配置参数**:
   - `signalingUrl`: `ws://localhost:8081`
   - `roomId`: `default-room`
   - `targetRenderer`: 拖拽一个Renderer组件（如Cube的Renderer）
4. **运行场景**

**预期Unity Console日志**:
```
✅ WebSocket连接成功
📩 收到信令消息: {"type":"joined","room":"default-room","role":"unity-receiver"}
✅ 已加入房间: default-room
📩 收到信令消息: {"type":"ready","room":"default-room"}
📡 房间已就绪，等待 Offer...
```

### 步骤 3: Web发送端测试
1. **打开浏览器**，访问: `TestHtml/Screen1020.html`
2. **观察连接日志**:
   - 应该显示 "✅ 已連接至信令伺服器"
   - 应该显示 "👋 已加入房間: default-room"
3. **点击"分享螢幕"按钮**
4. **允许浏览器权限请求**

**预期浏览器日志**:
```
[时间] ✅ 已連接至信令伺服器
[时间] 👋 已加入房間: default-room
[时间] 📡 房間就緒，準備發送 Offer...
[时间] 🖥️ 已啟動螢幕分享
[时间] 📤 發送 Offer 給 Unity
[时间] ✅ 收到 Unity Answer
```

### 步骤 4: 验证视频传输
1. **Unity端**: 检查Renderer是否显示视频内容
2. **服务器端**: 观察终端日志，应该显示消息转发
3. **Web端**: 检查视频预览是否正常

## 📊 预期消息流程

### 完整信令交换:
```
1. Web -> Server: {"type":"join","room":"default-room","role":"web-sender"}
2. Server -> Web: {"type":"joined","room":"default-room","role":"web-sender"}
3. Unity -> Server: {"type":"join","room":"default-room","role":"unity-receiver"}
4. Server -> Unity: {"type":"joined","room":"default-room","role":"unity-receiver"}
5. Server -> All: {"type":"ready","room":"default-room"}
6. Web -> Server: {"type":"offer","room":"default-room","from":"web-sender","sdp":"..."}
7. Server -> Unity: {"type":"offer","room":"default-room","from":"web-sender","sdp":"..."}
8. Unity -> Server: {"type":"answer","room":"default-room","from":"unity-receiver","sdp":"..."}
9. Server -> Web: {"type":"answer","room":"default-room","from":"unity-receiver","sdp":"..."}
10. Web -> Server: {"type":"candidate","room":"default-room","from":"web-sender","candidate":{...}}
11. Server -> Unity: {"type":"candidate","room":"default-room","from":"web-sender","candidate":{...}}
12. Unity -> Server: {"type":"candidate","room":"default-room","from":"unity-receiver","candidate":{...}}
13. Server -> Web: {"type":"candidate","room":"default-room","from":"unity-receiver","candidate":{...}}
```

## 🔍 服务器日志示例

**正常连接**:
```
🔌 新客户端连接
📨 收到消息: join from unknown
✅ web-sender joined room: default-room, peers: 1
🔌 新客户端连接
📨 收到消息: join from unknown
✅ unity-receiver joined room: default-room, peers: 2
📢 房间 default-room 已就绪，WebRTC 可以开始
📨 收到消息: offer from web-sender
📡 转发 offer from web-sender 到房间 default-room 的其他客户端
📨 收到消息: answer from unity-receiver
📡 转发 answer from unity-receiver 到房间 default-room 的其他客户端
```

## 🐛 常见问题排查

### 1. Unity连接失败
**症状**: Unity Console显示连接错误
**检查**:
- `signalingUrl` 是否为 `ws://localhost:8081`
- 服务器是否正在运行
- 防火墙是否阻止连接

### 2. Web端无法连接
**症状**: 浏览器显示连接失败
**检查**:
- 浏览器控制台是否有错误
- 服务器日志是否显示Web端连接
- 网络连接是否正常

### 3. 视频不显示
**症状**: Unity中Renderer没有显示视频
**检查**:
- `targetRenderer` 是否正确设置
- Unity Console是否显示 "🎥 收到远端视频流"
- WebRTC连接状态是否为 `connected`

### 4. 信令消息错误
**症状**: 服务器显示 "数据格式错误"
**检查**:
- 消息是否为有效JSON格式
- 消息是否包含必要的 `type` 字段
- 房间ID是否匹配

## ✅ 成功标志

1. **服务器**: 显示两个客户端连接，房间就绪
2. **Web端**: 显示"收到 Unity Answer"，视频预览正常
3. **Unity端**: 显示"收到 Offer"和"收到 ICE Candidate"，视频在Renderer上显示
4. **网络**: ICE连接状态为 `connected`

## 🔧 调试技巧

1. **开启详细日志**: 所有组件都有Debug.Log输出
2. **检查WebRTC状态**: 在浏览器开发者工具中查看连接状态
3. **验证信令**: 服务器会打印所有转发的消息
4. **测试ICE**: 确保STUN服务器可访问 (`stun:stun.l.google.com:19302`)

## 📝 测试检查清单

- [ ] 服务器运行在端口 8081
- [ ] Unity场景运行，TestScreenCon脚本已添加
- [ ] Unity Console显示WebSocket连接成功
- [ ] 浏览器可以访问 Screen1020.html
- [ ] Web端显示连接成功和房间加入
- [ ] 点击"分享螢幕"后Unity收到Offer
- [ ] Unity自动回复Answer
- [ ] ICE候选正常交换
- [ ] Unity中显示接收到的视频流

## 🎉 测试完成

如果所有步骤都成功，你应该能看到：
- Web端显示屏幕共享预览
- Unity端在Renderer上显示相同的视频内容
- 服务器日志显示完整的信令交换过程

恭喜！你的WebRTC三端对接系统已经成功运行！

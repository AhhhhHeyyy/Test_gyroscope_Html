# 🎯 本地WebRTC三端对接测试指南

## 📋 测试环境
- **信令服务器**: Node.js WebSocket (端口 8081)
- **发送端**: HTML5 WebRTC (Screen1020.html)
- **接收端**: Unity WebRTC (TestScreenCon.cs)

## 🚀 启动步骤

### 1. 启动信令服务器
```bash
node simplewebrtc-server.js
```
**预期输出**:
```
🚀 SimpleWebRTC 信令服务器启动成功!
🔌 WebSocket 端点: ws://localhost:8081
```

### 2. Unity 接收端配置
1. 在Unity中打开场景
2. 将 `TestScreenCon.cs` 脚本挂载到GameObject上
3. 配置参数：
   - `signalingUrl`: `ws://localhost:8081`
   - `roomId`: `default-room`
   - `targetRenderer`: 指定要显示视频的Renderer
4. 运行Unity场景

**预期Unity日志**:
```
收到信令消息: {"type":"joined","room":"default-room","role":"unity-receiver"}
已加入房间: default-room
收到信令消息: {"type":"ready","room":"default-room"}
房间已就绪，等待接收 Offer
```

### 3. HTML发送端测试
1. 打开浏览器访问: `TestHtml/Screen1020.html`
2. 点击"分享螢幕"或"啟用攝影機"
3. 允许浏览器权限请求

**预期浏览器日志**:
```
[时间] ✅ 已連接至信令伺服器
[时间] 👋 已加入房間: default-room
[时间] 📡 房間就緒，準備發送 Offer...
[时间] 🖥️ 已啟動螢幕分享
[时间] 📤 發送 Offer 給 Unity
[时间] ✅ 收到 Unity Answer
```

## 🔄 信令流程

### 完整消息流:
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

## 🐛 常见问题排查

### 1. 端口被占用
**错误**: `Error: listen EADDRINUSE: address already in use :::8081`
**解决**: 
```bash
netstat -ano | findstr :8081
taskkill /PID [进程ID] /F
```

### 2. Unity连接失败
**检查**:
- Unity脚本中的 `signalingUrl` 是否为 `ws://localhost:8081`
- 服务器是否正在运行
- Unity Console是否有错误日志

### 3. Web端无法连接
**检查**:
- 浏览器控制台是否有WebSocket连接错误
- 服务器日志是否显示客户端连接
- 防火墙是否阻止了8081端口

### 4. 视频不显示
**检查**:
- Unity的 `targetRenderer` 是否正确设置
- WebRTC连接状态是否为 `connected`
- ICE候选是否正常交换

## 📊 服务器日志示例

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

## ✅ 成功标志

1. **服务器**: 显示两个客户端连接，房间就绪
2. **Web端**: 显示"收到 Unity Answer"，视频预览正常
3. **Unity端**: 显示"收到 Offer"和"收到 ICE Candidate"，视频在Renderer上显示
4. **网络**: ICE连接状态为 `connected`

## 🔧 调试技巧

1. **开启详细日志**: 所有组件都有Debug.Log输出
2. **检查WebRTC状态**: 在浏览器开发者工具中查看 `pc.connectionState`
3. **验证信令**: 服务器会打印所有转发的消息
4. **测试ICE**: 确保STUN服务器可访问 (`stun:stun.l.google.com:19302`)

## 📝 注意事项

- 确保所有组件都连接到同一个房间 (`default-room`)
- Web端需要HTTPS或localhost才能访问摄像头/屏幕
- Unity需要正确配置WebRTC包和NativeWebSocket
- 本地测试不需要TURN服务器，但生产环境可能需要

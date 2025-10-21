const express = require('express');
const WebSocket = require('ws');
const http = require('http');
const path = require('path');

const app = express();
const server = http.createServer(app);

// 靜態檔案服務 - 指向TestHtml資料夾
app.use(express.static(path.join(__dirname, 'TestHtml')));

// 根路徑重導向到index.html
app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, 'TestHtml', 'index.html'));
});

// WebSocket伺服器
const wss = new WebSocket.Server({ server });

// 儲存所有連接的客戶端
const clients = new Set();

// 房間管理
const rooms = new Map(); // roomId -> Set<WebSocket>

// 連接統計
const stats = {
    totalConnections: 0,
    activeConnections: 0,
    totalMessages: 0,
    webrtcOffers: 0,
    webrtcAnswers: 0,
    webrtcCandidates: 0,
    startTime: Date.now()
};

wss.on('connection', (ws, req) => {
    console.log('🔌 新的WebSocket連接來自:', req.socket.remoteAddress);
    clients.add(ws);
    stats.totalConnections++;
    stats.activeConnections = clients.size;
    
    // 設置心跳保活
    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });
    
    // 不發送歡迎訊息，避免客戶端解析錯誤
    
    ws.on('message', (data, isBinary) => {
        try {
            // 只處理文字數據
            if (isBinary) {
                console.log('⚠️ 收到二進位數據，忽略');
                return;
            }
            
            // 解析JSON消息
            const text = (typeof data === 'string') ? data : data.toString('utf8');
            let msg;
            
            try {
                msg = JSON.parse(text);
            } catch (e) {
                console.log(`⚠️ JSON 解析失败，丢弃消息: ${text.substring(0, 100)}...`);
                return;
            }
            
            // 檢查消息類型
            if (!msg.type) {
                console.log(`⚠️ 收到無效消息，缺少type字段: ${JSON.stringify(msg)}`);
                return;
            }
            
            stats.totalMessages++;
            console.log(`📨 收到消息: ${msg.type} from ${msg.from || 'unknown'}`);
            
            // 處理房間加入
            if (msg.type === 'join') {
                const { room, role } = msg;
                ws.room = room;
                ws.role = role;
                
                // 檢查房間限制
                const peers = rooms.get(room) || new Set();
                const sameRole = Array.from(peers).find(p => p.role === role);
                if (sameRole) {
                    // 踢掉舊的
                    sameRole.close(1000, 'Replaced by new peer');
                }
                
                peers.add(ws);
                rooms.set(room, peers);
                
                // 發送加入確認
                ws.send(JSON.stringify({ 
                    type: 'joined', 
                    room, 
                    role,
                    peers: Array.from(peers).filter(p => p !== ws).map(p => p.role)
                }));
                
                console.log(`✅ ${role} joined room: ${room}, peers: ${peers.size}`);
                
                // 檢查房間是否已滿（2個 peer）
                if (peers.size === 2) {
                    console.log(`🤝 Room ${room} has both peers ready, notifying all`);
                    
                    // 通知所有同房 peer 準備就緒
                    for (const peer of peers) {
                        if (peer.readyState === WebSocket.OPEN) {
                            peer.send(JSON.stringify({
                                type: 'ready',
                                room: room,
                                message: 'Both peers joined, WebRTC can start'
                            }));
                        }
                    }
                }
                
                return;
            }
            
            // WebRTC 信令轉發
            if (['offer', 'answer', 'candidate'].includes(msg.type)) {
                if (!ws.room) return;
                
                const peers = rooms.get(ws.room) || new Set();
                
                // 添加 from 字段
                const forwardedMsg = {
                    ...msg,
                    from: ws.role || 'unknown'
                };
                
                for (const peer of peers) {
                    if (peer !== ws && peer.readyState === WebSocket.OPEN) {
                        peer.send(JSON.stringify(forwardedMsg));
                    }
                }
                
                // 更新統計
                if (msg.type === 'offer') stats.webrtcOffers++;
                else if (msg.type === 'answer') stats.webrtcAnswers++;
                else if (msg.type === 'candidate') stats.webrtcCandidates++;
                
                console.log(`📡 轉發 ${msg.type} from ${ws.role} to room ${ws.room}`);
                return;
            }
            
            // 處理其他消息類型
            if (msg.type === 'ready') {
                // 這是一個就緒信號，可以記錄但不轉發
                console.log(`📡 ${ws.role} is ready in room ${ws.room}`);
                return;
            }
            
            // 未知消息類型
            console.log(`⚠️ 未知消息類型: ${msg.type}`);
            
        } catch (error) {
            console.error('❌ 處理訊息錯誤:', error);
            ws.send(JSON.stringify({
                type: 'error',
                message: '數據格式錯誤',
                timestamp: Date.now()
            }));
        }
    });
    
    ws.on('close', (code, reason) => {
        console.log('🔌 WebSocket連接關閉:', code, reason.toString());
        clients.delete(ws);
        stats.activeConnections = clients.size;
        
        // 清理房間
        if (ws.room) {
            const peers = rooms.get(ws.room);
            if (peers) {
                peers.delete(ws);
                if (peers.size === 0) {
                    rooms.delete(ws.room);
                    console.log(`🧹 房間 ${ws.room} 已清空並刪除`);
                } else {
                    console.log(`👋 ${ws.role || 'client'} 離開房間 ${ws.room}，剩餘 ${peers.size} 人`);
                }
            }
        }
    });
    
    ws.on('error', (error) => {
        console.error('❌ WebSocket錯誤:', error);
        clients.delete(ws);
        stats.activeConnections = clients.size;
    });
});

// 健康檢查端點
app.get('/health', (req, res) => {
    const uptime = Date.now() - stats.startTime;
    res.json({
        status: 'ok',
        uptime: Math.floor(uptime / 1000),
        connections: {
            active: stats.activeConnections,
            total: stats.totalConnections
        },
        messages: {
            total: stats.totalMessages,
            webrtcOffers: stats.webrtcOffers,
            webrtcAnswers: stats.webrtcAnswers,
            webrtcCandidates: stats.webrtcCandidates
        },
        rooms: rooms.size,
        timestamp: Date.now()
    });
});

// API端點 - 獲取詳細狀態
app.get('/api/status', (req, res) => {
    const uptime = Date.now() - stats.startTime;
    const memoryUsage = process.memoryUsage();
    
    res.json({
        service: 'WebRTC Signaling Server',
        version: '1.0.0',
        uptime: Math.floor(uptime / 1000),
        connections: {
            active: stats.activeConnections,
            total: stats.totalConnections
        },
        messages: {
            total: stats.totalMessages,
            webrtcOffers: stats.webrtcOffers,
            webrtcAnswers: stats.webrtcAnswers,
            webrtcCandidates: stats.webrtcCandidates
        },
        rooms: rooms.size,
        memory: {
            used: Math.round(memoryUsage.heapUsed / 1024 / 1024),
            total: Math.round(memoryUsage.heapTotal / 1024 / 1024),
            external: Math.round(memoryUsage.external / 1024 / 1024)
        },
        features: {
            webrtcSignaling: true,
            staticFileServing: true
        },
        timestamp: Date.now()
    });
});

// 心跳保活
setInterval(() => {
    wss.clients.forEach(ws => {
        if (!ws.isAlive) return ws.terminate();
        ws.isAlive = false;
        ws.ping();
    });
}, 25000);

// 定期清理無效連接
setInterval(() => {
    const beforeCount = clients.size;
    clients.forEach(client => {
        if (client.readyState === WebSocket.CLOSED || client.readyState === WebSocket.CLOSING) {
            clients.delete(client);
        }
    });
    stats.activeConnections = clients.size;
    
    if (beforeCount !== clients.size) {
        console.log(`🧹 清理無效連接: ${beforeCount} -> ${clients.size}`);
    }
}, 30000);

// 定期狀態報告
setInterval(() => {
    const uptime = Math.floor((Date.now() - stats.startTime) / 1000);
    console.log(`📊 服務狀態: 運行時間 ${uptime}s, 活躍連接 ${clients.size}, 總訊息 ${stats.totalMessages}`);
    console.log(`📡 WebRTC統計: Offers ${stats.webrtcOffers}, Answers ${stats.webrtcAnswers}, Candidates ${stats.webrtcCandidates}`);
}, 60000);

const PORT = process.env.PORT || 8081;
server.listen(PORT, () => {
    console.log('🚀 WebRTC 信令伺服器啟動成功!');
    console.log(`📱 靜態檔案服務: http://localhost:${PORT}`);
    console.log(`🔌 WebSocket端點: ws://localhost:${PORT}`);
    console.log(`❤️ 健康檢查: http://localhost:${PORT}/health`);
    console.log(`📊 狀態監控: http://localhost:${PORT}/api/status`);
    console.log(`📺 支援功能: WebRTC 信令交換`);
});

// 優雅關閉
process.on('SIGTERM', () => {
    console.log('🛑 收到SIGTERM信號，正在關閉伺服器...');
    server.close(() => {
        console.log('✅ 伺服器已優雅關閉');
        process.exit(0);
    });
});

process.on('SIGINT', () => {
    console.log('🛑 收到SIGINT信號，正在關閉伺服器...');
    server.close(() => {
        console.log('✅ 伺服器已優雅關閉');
        process.exit(0);
    });
});

// 未捕獲的異常處理
process.on('uncaughtException', (error) => {
    console.error('💥 未捕獲的異常:', error);
    process.exit(1);
});

process.on('unhandledRejection', (reason, promise) => {
    console.error('💥 未處理的Promise拒絕:', reason);
    process.exit(1);
});

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

// 連接統計
const stats = {
    totalConnections: 0,
    activeConnections: 0,
    totalMessages: 0,
    startTime: Date.now()
};

wss.on('connection', (ws, req) => {
    console.log('🔌 新的WebSocket連接來自:', req.socket.remoteAddress);
    clients.add(ws);
    stats.totalConnections++;
    stats.activeConnections = clients.size;
    
    // 發送歡迎訊息
    ws.send(JSON.stringify({
        type: 'connection',
        message: 'WebSocket連接已建立',
        timestamp: Date.now(),
        clientId: stats.totalConnections
    }));
    
    ws.on('message', (message) => {
        try {
            const msg = JSON.parse(message);
            stats.totalMessages++;

            // 處理 join 房間請求（Unity 連線後會發送）
            if (msg.type === 'join') {
                console.log(`🚪 客戶端加入房間: ${msg.room} as ${msg.role}`);
                ws.send(JSON.stringify({ type: 'joined', message: `已加入房間 ${msg.room}`, timestamp: Date.now() }));
                // 如果已有其他客戶端，通知 ready
                if (clients.size >= 2) {
                    clients.forEach(client => {
                        if (client.readyState === WebSocket.OPEN) {
                            client.send(JSON.stringify({ type: 'ready', message: '所有客戶端已就緒', timestamp: Date.now() }));
                        }
                    });
                }
                return;
            }

            // 處理手機搶佔控制權宣告（不需廣播）
            if (msg.type === 'claim') {
                console.log('📱 手機宣告控制權');
                ws.send(JSON.stringify({ type: 'ack', message: '控制權已確認', timestamp: Date.now() }));
                return;
            }

            let out;
            const now = Date.now();
            if (msg.type === 'gyroscope') {
                out = {
                    type: 'gyroscope',
                    data: {
                        alpha: msg.alpha,
                        beta: msg.beta,
                        gamma: msg.gamma,
                        unityY: msg.unityY,
                        qx: msg.qx,
                        qy: msg.qy,
                        qz: msg.qz,
                        qw: msg.qw,
                        timestamp: msg.timestamp || now,
                        clientId: stats.totalConnections
                    },
                    timestamp: now,
                    clientId: stats.totalConnections
                };
            } else if (msg.type === 'shake') {
                out = { type: 'shake', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else if (msg.type === 'spin') {
                out = { type: 'spin', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else if (msg.type === 'spin_mode') {
                out = { type: 'spin_mode', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else if (msg.type === 'position') {
                out = { type: 'position', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else if (msg.type === 'ar_camera_pose') {
                out = { type: 'ar_camera_pose', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else if (msg.type === 'acceleration') {
                out = { type: 'acceleration', data: msg.data, timestamp: now, clientId: stats.totalConnections };
            } else {
                // 預設當作陀螺儀角度（向後相容）
                out = {
                    type: 'gyroscope',
                    data: { alpha: msg.alpha, beta: msg.beta, gamma: msg.gamma, timestamp: msg.timestamp, clientId: stats.totalConnections },
                    timestamp: now,
                    clientId: stats.totalConnections
                };
            }

            // 廣播給所有其他客戶端（pre-serialize 只算一次）
            const outStr = JSON.stringify(out);
            clients.forEach(client => {
                if (client !== ws && client.readyState === WebSocket.OPEN) {
                    client.send(outStr);
                }
            });
            
        } catch (error) {
            console.error('❌ 解析訊息錯誤:', error);
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
        messages: stats.totalMessages,
        timestamp: Date.now()
    });
});

// API端點 - 獲取詳細狀態
app.get('/api/status', (req, res) => {
    const uptime = Date.now() - stats.startTime;
    const memoryUsage = process.memoryUsage();
    
    res.json({
        service: 'Gyroscope WebSocket Server',
        version: '1.0.0',
        uptime: Math.floor(uptime / 1000),
        connections: {
            active: stats.activeConnections,
            total: stats.totalConnections
        },
        messages: stats.totalMessages,
        memory: {
            used: Math.round(memoryUsage.heapUsed / 1024 / 1024),
            total: Math.round(memoryUsage.heapTotal / 1024 / 1024),
            external: Math.round(memoryUsage.external / 1024 / 1024)
        },
        timestamp: Date.now()
    });
});

// 保持活躍端點
app.get('/api/ping', (req, res) => {
    res.json({
        status: 'pong',
        timestamp: Date.now(),
        uptime: Math.floor((Date.now() - stats.startTime) / 1000)
    });
});

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
}, 30000); // 每30秒清理一次

// 定期狀態報告
setInterval(() => {
    const uptime = Math.floor((Date.now() - stats.startTime) / 1000);
    console.log(`📊 服務狀態: 運行時間 ${uptime}s, 活躍連接 ${clients.size}, 總訊息 ${stats.totalMessages}`);
}, 60000); // 每分鐘報告一次

const PORT = process.env.PORT || 8080;
server.listen(PORT, () => {
    console.log('🚀 陀螺儀WebSocket伺服器啟動成功!');
    console.log(`📱 靜態檔案服務: http://localhost:${PORT}`);
    console.log(`🔌 WebSocket端點: ws://localhost:${PORT}`);
    console.log(`❤️ 健康檢查: http://localhost:${PORT}/health`);
    console.log(`📊 狀態監控: http://localhost:${PORT}/api/status`);
    console.log(`🏓 保持活躍: http://localhost:${PORT}/api/ping`);
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

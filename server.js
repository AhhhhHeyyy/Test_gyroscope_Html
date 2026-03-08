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
            
            let out;
            if (msg.type === 'shake') {
                // 處理搖晃數據
                console.log('📳 收到搖晃數據:', {
                    count: msg.data?.count,
                    intensity: msg.data?.intensity,
                    shakeType: msg.data?.shakeType,
                    clientId: stats.totalConnections
                });
                
                out = { 
                    type: 'shake', 
                    data: msg.data, 
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else if (msg.type === 'spin') {
                // 🌀 新增旋轉事件處理
                console.log('🎯 收到旋轉事件:', {
                    angle: msg.data?.angle,
                    triggered: msg.data?.triggered,
                    clientId: stats.totalConnections
                });
                
                out = {
                    type: 'spin',
                    data: msg.data,
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else if (msg.type === 'spin_mode') {
                // 🎚️ 處理旋鈕模式（包含 Unity 發出的 toggle_request）
                console.log('🎚️ 收到旋鈕模式訊息:', {
                    mode: msg.data?.mode,
                    snapAngle: msg.data?.snapAngle,
                    label: msg.data?.label,
                    timestamp: msg.data?.timestamp,
                    fromClientId: stats.totalConnections
                });

                // 原樣轉發給其他 client（Unity / 手機彼此都能收到）
                out = {
                    type: 'spin_mode',
                    data: msg.data,
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else if (msg.type === 'position') {
                // 📍 處理 8th Wall 位置數據
                console.log('📍 收到位置數據:', {
                    position: msg.data?.position,
                    delta: msg.data?.delta,
                    clientId: stats.totalConnections
                });
                
                out = {
                    type: 'position',
                    data: msg.data,
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else if (msg.type === 'ar_camera_pose') {
                // 📷 AR 頁面：相機相對 Marker 的位置/旋轉，轉發給 Unity
                out = {
                    type: 'ar_camera_pose',
                    data: msg.data,
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else {
                // 預設當作陀螺儀角度（向後相容）
                console.log('📱 收到陀螺儀數據:', {
                    alpha: msg.alpha,
                    beta: msg.beta,
                    gamma: msg.gamma,
                    clientId: stats.totalConnections
                });
                
                const gyroData = {
                    alpha: msg.alpha,
                    beta: msg.beta,
                    gamma: msg.gamma,
                    timestamp: msg.timestamp,
                    clientId: stats.totalConnections
                };
                
                out = { 
                    type: 'gyroscope', 
                    data: gyroData, 
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            }
            
            // 廣播給所有其他客戶端（包括Unity）
            clients.forEach(client => {
                if (client !== ws && client.readyState === WebSocket.OPEN) {
                    client.send(JSON.stringify(out));
                }
            });
            
            // 回應發送者確認收到
            ws.send(JSON.stringify({
                type: 'ack',
                message: '數據已廣播',
                timestamp: Date.now(),
                clientsCount: clients.size
            }));
            
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

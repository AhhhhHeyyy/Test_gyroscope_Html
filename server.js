const express = require('express');
const WebSocket = require('ws');
const http = require('http');
const path = require('path');

const app = express();
const server = http.createServer(app);

// 靜態檔案服務 - 指向TestHtml資料夾
app.use(express.static(path.join(__dirname, 'Test_gyroscope_Html', 'TestHtml')));

// 根路徑重導向到index.html
app.get('/', (req, res) => {
    res.sendFile(path.join(__dirname, 'Test_gyroscope_Html', 'TestHtml', 'index.html'));
});

// WebSocket伺服器
const wss = new WebSocket.Server({ server });

// 儲存所有連接的客戶端
const clients = new Set();

wss.on('connection', (ws, req) => {
    console.log('新的WebSocket連接來自:', req.socket.remoteAddress);
    clients.add(ws);
    
    // 發送歡迎訊息
    ws.send(JSON.stringify({
        type: 'connection',
        message: 'WebSocket連接已建立',
        timestamp: Date.now()
    }));
    
    ws.on('message', (message) => {
        try {
            const data = JSON.parse(message);
            console.log('收到陀螺儀數據:', data);
            
            // 廣播給所有其他客戶端（包括Unity）
            clients.forEach(client => {
                if (client !== ws && client.readyState === WebSocket.OPEN) {
                    client.send(JSON.stringify({
                        type: 'gyroscope',
                        data: data,
                        timestamp: Date.now()
                    }));
                }
            });
            
            // 回應發送者確認收到
            ws.send(JSON.stringify({
                type: 'ack',
                message: '數據已廣播',
                timestamp: Date.now()
            }));
            
        } catch (error) {
            console.error('解析訊息錯誤:', error);
            ws.send(JSON.stringify({
                type: 'error',
                message: '數據格式錯誤',
                timestamp: Date.now()
            }));
        }
    });
    
    ws.on('close', () => {
        console.log('WebSocket連接關閉');
        clients.delete(ws);
    });
    
    ws.on('error', (error) => {
        console.error('WebSocket錯誤:', error);
        clients.delete(ws);
    });
});

// 健康檢查端點
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        timestamp: Date.now(),
        clients: clients.size
    });
});

// API端點 - 獲取連接狀態
app.get('/api/status', (req, res) => {
    res.json({
        connectedClients: clients.size,
        uptime: process.uptime(),
        timestamp: Date.now()
    });
});

const PORT = process.env.PORT || 3000;
server.listen(PORT, () => {
    console.log(`🚀 陀螺儀WebSocket伺服器運行在端口 ${PORT}`);
    console.log(`📱 靜態檔案服務: http://localhost:${PORT}`);
    console.log(`🔌 WebSocket端點: ws://localhost:${PORT}`);
    console.log(`❤️ 健康檢查: http://localhost:${PORT}/health`);
});

// 優雅關閉
process.on('SIGTERM', () => {
    console.log('收到SIGTERM信號，正在關閉伺服器...');
    server.close(() => {
        console.log('伺服器已關閉');
        process.exit(0);
    });
});

process.on('SIGINT', () => {
    console.log('收到SIGINT信號，正在關閉伺服器...');
    server.close(() => {
        console.log('伺服器已關閉');
        process.exit(0);
    });
});

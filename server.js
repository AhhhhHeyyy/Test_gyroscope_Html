const express = require('express');
const WebSocket = require('ws');
const http = require('http');
const path = require('path');

const app = express();
const server = http.createServer(app);

// 靜態檔案服務 - 指向TestHtml資料夾
app.use(express.static(path.join(__dirname, 'TestHtml')));

// 實例識別（用於診斷是否多實例）
const INSTANCE = process.env.RAILWAY_STATIC_URL || process.env.HOSTNAME || String(process.pid);

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

// SimpleWebRTC 消息格式转换函数 - 使用管道分隔格式
function convertToSimpleWebRTCFormat(msg, senderRole) {
    const senderPeerId = msg.from || senderRole;
    const receiverPeerId = msg.to || "ALL";
    const connectionCount = clients.size;
    const isVideoAudioSender = senderRole === 'web-sender';

    switch (msg.type) {
        case 'offer':
            return `${msg.type.toUpperCase()}|${senderPeerId}|${receiverPeerId}|${msg.sdp}|${connectionCount}|${isVideoAudioSender}`;
        case 'answer':
            return `${msg.type.toUpperCase()}|${senderPeerId}|${receiverPeerId}|${msg.sdp}|${connectionCount}|${isVideoAudioSender}`;
        case 'candidate':
            const candidateData = JSON.stringify({
                candidate: msg.candidate,
                sdpMLineIndex: msg.sdpMLineIndex,
                sdpMid: msg.sdpMid
            });
            return `CANDIDATE|${senderPeerId}|${receiverPeerId}|${candidateData}|${connectionCount}|${isVideoAudioSender}`;
        case 'join':
            return `NEWPEER|${senderPeerId}|${receiverPeerId}|${JSON.stringify({room: msg.room, role: msg.role})}|${connectionCount}|${isVideoAudioSender}`;
        case 'ready':
            return `OTHER|${senderPeerId}|${receiverPeerId}|${JSON.stringify({type: 'ready', room: msg.room})}|${connectionCount}|${isVideoAudioSender}`;
        default:
            return `OTHER|${senderPeerId}|${receiverPeerId}|${JSON.stringify(msg)}|${connectionCount}|${isVideoAudioSender}`;
    }
}

// 解析 SimpleWebRTC 管道分隔格式消息
function parseSimpleWebRTCMessage(messageString) {
    const parts = messageString.split('|');
    if (parts.length < 6) return null;
    
    return {
        type: parts[0],
        senderPeerId: parts[1],
        receiverPeerId: parts[2],
        message: parts[3],
        connectionCount: parseInt(parts[4]),
        isVideoAudioSender: parts[5] === 'true'
    };
}

// 格式转换器：确保所有消息都符合 SimpleWebRTC 标准格式
function adaptToSimpleWebRTC(msg) {
    // 1) 若已是标准格式，直接返回
    if (["join", "leave", "offer", "answer", "candidate", "broadcast", 
         "peer-joined", "peer-left"].includes(msg.type)) {
        return msg;
    }

    // 2) 兼容旧格式转换
    if (msg.action === "join" && msg.id) {
        return { type: "join", from: msg.id };
    }

    if (msg.signal === "offer" && msg.sender && msg.target && msg.sdp) {
        return { type: "offer", from: msg.sender, to: msg.target, sdp: msg.sdp };
    }

    if (msg.signal === "answer" && msg.sender && msg.target && msg.sdp) {
        return { type: "answer", from: msg.sender, to: msg.target, sdp: msg.sdp };
    }

    if (msg.ice && msg.sender && msg.target) {
        return { type: "candidate", from: msg.sender, to: msg.target, candidate: msg.ice };
    }

    // 3) 丢弃不兼容的消息，避免发送 type:"error" 给 Unity
    console.log(`⚠️ 丢弃不兼容的消息: ${JSON.stringify(msg)}`);
    return null;
}

// 連接統計
const stats = {
    totalConnections: 0,
    activeConnections: 0,
    totalMessages: 0,
    screenCaptureMessages: 0,
    gyroscopeMessages: 0,
    shakeMessages: 0,
    rooms: 0,
    webrtcOffers: 0,
    webrtcAnswers: 0,
    webrtcCandidates: 0,
    webrtcFallbacks: 0,
    startTime: Date.now()
};

wss.on('connection', (ws, req) => {
    console.log('🔌 新的WebSocket連接來自:', req.socket.remoteAddress, 'instance=', INSTANCE);
    clients.add(ws);
    stats.totalConnections++;
    stats.activeConnections = clients.size;
    
    // 設置心跳保活
    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });
    
    // 發送歡迎訊息
    ws.send(JSON.stringify({
        type: 'connection',
        message: 'WebSocket連接已建立',
        timestamp: Date.now(),
        clientId: stats.totalConnections,
        instance: INSTANCE
    }));
    
    // ws@8+ 使用 (data, isBinary) 簽名；文字常為 Buffer，但 isBinary=false
    ws.on('message', (data, isBinary) => {
        try {
            // 二進位數據：僅用於螢幕捕獲幀
            if (isBinary) {
                // 處理二進位螢幕捕獲數據
                if (ws.screenCaptureHeader) {
                    const header = ws.screenCaptureHeader;
                    const bytes = Buffer.isBuffer(data) ? new Uint8Array(data) : new Uint8Array();
                    const imageData = Array.from(bytes);
                    
                    stats.screenCaptureMessages++;
                    console.log('📺 收到螢幕捕獲二進位數據:', {
                        size: header.size,
                        timestamp: header.timestamp,
                        clientId: header.clientId,
                        dataLength: imageData.length
                    });
                    
                    const out = {
                        type: 'screen_capture',
                        clientId: header.clientId,
                        timestamp: header.timestamp,
                        size: header.size,
                        image: imageData
                    };
                    
                    // 廣播給所有客戶端（包含發送者，以便偵錯）
                    clients.forEach(client => {
                        if (client.readyState === WebSocket.OPEN) {
                            try {
                                client.send(JSON.stringify(out));
                            } catch (e) {
                                console.error('❌ 廣播失敗:', e);
                            }
                        }
                    });
                    console.log('📣 廣播訊息: screen_capture → clients:', clients.size, 'size=', out.size, 'instance=', INSTANCE);
                    
                    // 清除header
                    delete ws.screenCaptureHeader;
                }
                return;
            }
            
            // 文字數據：轉字串再解析
            const text = (typeof data === 'string') ? data : data.toString('utf8');
            
            // 检查是否为 SimpleWebRTC 管道分隔格式
            if (text.includes('|') && text.split('|').length >= 6) {
                const simpleMsg = parseSimpleWebRTCMessage(text);
                if (simpleMsg) {
                    console.log(`📨 收到 SimpleWebRTC 消息: ${simpleMsg.type} from ${simpleMsg.senderPeerId}`);
                    
                    // 处理 SimpleWebRTC 格式的消息
                    if (simpleMsg.type === 'NEWPEER') {
                        // 处理加入房间
                        const joinData = JSON.parse(simpleMsg.message);
                        const { room, role } = joinData;
                        ws.room = room;
                        ws.role = role;
                        
                        const peers = rooms.get(room) || new Set();
                        const sameRole = Array.from(peers).find(p => p.role === role);
                        if (sameRole) {
                            sameRole.close(1000, 'Replaced by new peer');
                        }
                        
                        peers.add(ws);
                        rooms.set(room, peers);
                        stats.rooms = rooms.size;
                        
                        // 发送 SimpleWebRTC 格式的确认
                        ws.send(convertToSimpleWebRTCFormat({
                            type: 'join',
                            room: room,
                            role: role
                        }, role));
                        
                        // 发送标准格式的 peer-joined 通知
                        for (const peer of peers) {
                            if (peer !== ws && peer.readyState === WebSocket.OPEN) {
                                peer.send(JSON.stringify({
                                    type: 'peer-joined',
                                    from: role
                                }));
                            }
                        }
                        
                        console.log(`✅ ${role} joined room: ${room}, peers: ${peers.size}`);
                        
                        // 检查房间是否已满
                        if (peers.size === 2) {
                            console.log(`🤝 Room ${room} has both peers ready, notifying all`);
                            for (const peer of peers) {
                                if (peer.readyState === WebSocket.OPEN) {
                                    const readyMsg = convertToSimpleWebRTCFormat({
                                        type: 'ready',
                                        room: room
                                    }, peer.role);
                                    peer.send(readyMsg);
                                }
                            }
                        }
                        return;
                    }
                    
                    // 处理 WebRTC 信令消息
                    if (['OFFER', 'ANSWER', 'CANDIDATE'].includes(simpleMsg.type)) {
                        if (!ws.room) return;
                        
                        const peers = rooms.get(ws.room) || new Set();
                        
                        for (const peer of peers) {
                            if (peer !== ws && peer.readyState === WebSocket.OPEN) {
                                if (simpleMsg.receiverPeerId !== 'ALL' && peer.role !== simpleMsg.receiverPeerId) {
                                    continue;
                                }
                                peer.send(text); // 直接转发原始消息
                            }
                        }
                        
                        console.log(`📡 轉發 ${simpleMsg.type} from ${simpleMsg.senderPeerId} to room ${ws.room}`);
                        return;
                    }
                }
            }
            
            // 尝试解析为 JSON 格式（传统格式）
            let msg;
            try {
                msg = JSON.parse(text);
            } catch (e) {
                console.log(`⚠️ JSON 解析失败，丢弃消息: ${text.substring(0, 100)}...`);
                return; // 丢弃无法解析的消息
            }
            
            // 使用格式转换器，确保符合 SimpleWebRTC 标准
            const adaptedMsg = adaptToSimpleWebRTC(msg);
            if (!adaptedMsg) {
                return; // 丢弃不兼容的消息，避免发送 type:"error" 给 Unity
            }
            
            stats.totalMessages++;
            
            // 房間加入
            if (adaptedMsg.type === 'join') {
                const { room, role } = adaptedMsg; // role: 'web-sender' / 'unity-receiver'
                ws.room = room;
                ws.role = role;
                
                // 檢查房間限制
                const peers = rooms.get(room) || new Set();
                const sameRole = Array.from(peers).find(p => p.role === role);
                if (sameRole) {
                    // 踢掉舊的或拒絕新的
                    sameRole.close(1000, 'Replaced by new peer');
                }
                
                peers.add(ws);
                rooms.set(room, peers);
                stats.rooms = rooms.size;
                
                // 发送 SimpleWebRTC 格式的加入确认
                const joinConfirm = convertToSimpleWebRTCFormat({
                    type: 'join',
                    room: room,
                    role: role
                }, role);
                
                ws.send(joinConfirm);
                
                // 发送传统格式的确认（向后兼容）
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
                            // 发送 SimpleWebRTC 格式的就绪消息
                            const readyMsg = convertToSimpleWebRTCFormat({
                                type: 'ready',
                                room: room
                            }, peer.role);
                            peer.send(readyMsg);
                            
                            // 发送传统格式的就绪消息（向后兼容）
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
            
            // WebRTC 原生三型別轉發
            if (['offer', 'answer', 'candidate'].includes(adaptedMsg.type)) {
                if (!ws.room) return;
                
                const peers = rooms.get(ws.room) || new Set();
                
                // 转换消息格式为 SimpleWebRTC 兼容格式
                const simpleWebRTCMsg = convertToSimpleWebRTCFormat(adaptedMsg, ws.role);
                
                // 添加传统格式的 from/to 字段（向后兼容）
                const enhancedMsg = {
                    ...adaptedMsg,
                    from: ws.role || 'unknown',
                    to: adaptedMsg.to || 'all'
                };
                
                for (const peer of peers) {
                    if (peer !== ws && peer.readyState === WebSocket.OPEN) {
                        // 如果指定了 to 字段，只发送给匹配的 peer
                        if (simpleWebRTCMsg.receiverPeerId !== 'all' && peer.role !== simpleWebRTCMsg.receiverPeerId) {
                            continue;
                        }
                        
                        // 发送 SimpleWebRTC 格式消息（管道分隔格式）
                        peer.send(simpleWebRTCMsg);
                        
                        // 发送传统格式消息（向后兼容）
                        peer.send(JSON.stringify(enhancedMsg));
                    }
                }
                
                // 更新統計
                if (adaptedMsg.type === 'offer') stats.webrtcOffers++;
                else if (adaptedMsg.type === 'answer') stats.webrtcAnswers++;
                else if (adaptedMsg.type === 'candidate') stats.webrtcCandidates++;
                
                console.log(`📡 轉發 ${adaptedMsg.type} from ${ws.role} to room ${ws.room} (SimpleWebRTC格式)`);
                return;
            }
            
            let out;
            if (adaptedMsg.type === 'screen_capture_header') {
                // 儲存螢幕捕獲header，等待二進位數據
                ws.screenCaptureHeader = adaptedMsg;
                console.log('📺 收到螢幕捕獲header:', {
                    clientId: adaptedMsg.clientId,
                    size: adaptedMsg.size,
                    timestamp: adaptedMsg.timestamp
                });
                return; // 不廣播，等待二進位數據
            } else if (adaptedMsg.type === 'shake') {
                // 處理搖晃數據
                stats.shakeMessages++;
                console.log('📳 收到搖晃數據:', {
                    count: adaptedMsg.data?.count,
                    intensity: adaptedMsg.data?.intensity,
                    shakeType: adaptedMsg.data?.shakeType,
                    clientId: stats.totalConnections
                });
                
                out = { 
                    type: 'shake', 
                    data: adaptedMsg.data, 
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            } else {
                // 預設當作陀螺儀角度（向後相容）
                stats.gyroscopeMessages++;
                console.log('📱 收到陀螺儀數據:', {
                    alpha: adaptedMsg.alpha,
                    beta: adaptedMsg.beta,
                    gamma: adaptedMsg.gamma,
                    clientId: stats.totalConnections
                });
                
                const gyroData = {
                    alpha: adaptedMsg.alpha,
                    beta: adaptedMsg.beta,
                    gamma: adaptedMsg.gamma,
                    timestamp: adaptedMsg.timestamp,
                    clientId: stats.totalConnections
                };
                
                out = { 
                    type: 'gyroscope', 
                    data: gyroData, 
                    timestamp: Date.now(),
                    clientId: stats.totalConnections
                };
            }
            
            // 廣播給所有客戶端（包括Unity，含發送者以便偵錯）
            clients.forEach(client => {
                if (client.readyState === WebSocket.OPEN) {
                    try {
                        client.send(JSON.stringify(out));
                    } catch (e) {
                        console.error('❌ 廣播失敗:', e);
                    }
                }
            });
            console.log('📣 廣播訊息:', out.type, '→ clients:', clients.size, 'instance=', INSTANCE);
            
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
        
        // 清理房間
        if (ws.room) {
            const peers = rooms.get(ws.room);
            if (peers) {
                peers.delete(ws);
                if (peers.size === 0) {
                    rooms.delete(ws.room);
                    stats.rooms = rooms.size;
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
            gyroscope: stats.gyroscopeMessages,
            shake: stats.shakeMessages,
            screenCapture: stats.screenCaptureMessages
        },
        timestamp: Date.now()
    });
});

// API端點 - 獲取詳細狀態
app.get('/api/status', (req, res) => {
    const uptime = Date.now() - stats.startTime;
    const memoryUsage = process.memoryUsage();
    
    res.json({
        service: 'Gyroscope & Screen Capture WebSocket Server',
        version: '2.1.0',
        uptime: Math.floor(uptime / 1000),
        connections: {
            active: stats.activeConnections,
            total: stats.totalConnections
        },
        messages: {
            total: stats.totalMessages,
            gyroscope: stats.gyroscopeMessages,
            shake: stats.shakeMessages,
            screenCapture: stats.screenCaptureMessages,
            webrtcOffers: stats.webrtcOffers,
            webrtcAnswers: stats.webrtcAnswers,
            webrtcCandidates: stats.webrtcCandidates
        },
        rooms: stats.rooms,
        memory: {
            used: Math.round(memoryUsage.heapUsed / 1024 / 1024),
            total: Math.round(memoryUsage.heapTotal / 1024 / 1024),
            external: Math.round(memoryUsage.external / 1024 / 1024)
        },
        features: {
            gyroscope: true,
            shakeDetection: true,
            screenCapture: true,
            webrtcSignaling: true
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
}, 30000); // 每30秒清理一次

// 定期狀態報告
setInterval(() => {
    const uptime = Math.floor((Date.now() - stats.startTime) / 1000);
    console.log(`📊 服務狀態: 運行時間 ${uptime}s, 活躍連接 ${clients.size}, 總訊息 ${stats.totalMessages}`);
    console.log(`📱 數據統計: 陀螺儀 ${stats.gyroscopeMessages}, 搖晃 ${stats.shakeMessages}, 螢幕捕獲 ${stats.screenCaptureMessages}`);
}, 60000); // 每分鐘報告一次

const PORT = process.env.PORT || 8081;
server.listen(PORT, () => {
    console.log('🚀 陀螺儀 & 螢幕捕獲 WebSocket伺服器啟動成功!');
    console.log(`📱 靜態檔案服務: http://localhost:${PORT}`);
    console.log(`🔌 WebSocket端點: ws://localhost:${PORT}`);
    console.log(`❤️ 健康檢查: http://localhost:${PORT}/health`);
    console.log(`📊 狀態監控: http://localhost:${PORT}/api/status`);
    console.log(`🏓 保持活躍: http://localhost:${PORT}/api/ping`);
    console.log(`📺 支援功能: 陀螺儀、搖晃偵測、螢幕捕獲串流`);
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

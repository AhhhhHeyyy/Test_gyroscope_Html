/**
 * gyro-relay.js  2026-05-08
 * UDP(9999) 接收手機封包 → WebSocket 轉發到瀏覽器
 * HTTP(3000) 同時提供 gyro-demo.html
 *
 * 啟動：node gyro-relay.js
 */
const dgram  = require('dgram');
const http   = require('http');
const fs     = require('fs');
const path   = require('path');
const os     = require('os');
const { WebSocketServer } = require('ws');

const UDP_PORT  = 9999;
const HTTP_PORT = 3000;

// ── HTTP：只服務 gyro-demo.html ──────────────────────────────
const httpServer = http.createServer((req, res) => {
  const filePath = path.join(__dirname, 'gyro-demo.html');
  fs.readFile(filePath, (err, data) => {
    if (err) { res.writeHead(500); res.end('gyro-demo.html 找不到'); return; }
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(data);
  });
});

// ── WebSocket：掛在同一個 HTTP server 上 ────────────────────
const wss = new WebSocketServer({ server: httpServer });
wss.on('connection', () => process.stdout.write('\r[WS] 瀏覽器已連線          \n'));

function broadcast(obj) {
  if (!wss.clients.size) return;
  const msg = JSON.stringify(obj);
  for (const c of wss.clients)
    if (c.readyState === 1) c.send(msg);
}

// ── UDP：接收手機 28-byte Big-Endian 封包 ───────────────────
const udp = dgram.createSocket('udp4');
let pkt = 0, hz = 0, hzTs = Date.now();

udp.on('message', buf => {
  if (buf.length !== 28 && buf.length !== 16) return;

  pkt++;
  const now = Date.now();
  if (now - hzTs >= 1000) {
    hz = pkt; pkt = 0; hzTs = now;
    process.stdout.write(`\r[UDP] ${hz} Hz | WS 客戶端: ${wss.clients.size}   `);
  }

  broadcast({
    qx: buf.readFloatBE(0),
    qy: buf.readFloatBE(4),
    qz: buf.readFloatBE(8),
    qw: buf.readFloatBE(12),
    ax: buf.length >= 28 ? buf.readFloatBE(16) : 0,
    ay: buf.length >= 28 ? buf.readFloatBE(20) : 0,
    az: buf.length >= 28 ? buf.readFloatBE(24) : 0,
  });
});

udp.bind(UDP_PORT, () => console.log(`[UDP] 監聽 port ${UDP_PORT}`));

// ── 啟動 ────────────────────────────────────────────────────
httpServer.listen(HTTP_PORT, '0.0.0.0', () => {
  const ip = getLocalIP();
  console.log('\n========================================');
  console.log(`  瀏覽器開啟 → http://localhost:${HTTP_PORT}`);
  console.log(`  手機 App   → IP: ${ip}  Port: ${UDP_PORT}`);
  console.log('========================================\n');
});

function getLocalIP() {
  for (const nets of Object.values(os.networkInterfaces()))
    for (const n of nets)
      if (n.family === 'IPv4' && !n.internal) return n.address;
  return 'localhost';
}

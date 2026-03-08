# TestHtml 部署說明（Railway）

專案已設定好：`server.js` 會把 **TestHtml** 當成靜態目錄提供，所以 `ar-threex-camera.html` 和 `data/data/` 會一併部署。

---

## 方式一：用 Railway CLI 部署（推薦）

### 1. 安裝並登入 Railway

```bash
npm install -g @railway/cli
railway login
```

（會開瀏覽器完成登入。）

### 2. 在專案根目錄部署

在 **專案根目錄**（有 `server.js`、`package.json`、`TestHtml` 的那一層）執行：

```bash
cd c:\Users\user\Desktop\School\Project1141
railway link
railway up
```

若尚未在 Railway 建立專案，可先執行：

```bash
railway init
```

再執行 `railway up`。

### 3. 取得網址

部署完成後在 Railway 儀表板或終端會看到網址，例如：

- `https://testgyroscopehtml-production.up.railway.app`

AR 頁面與資料路徑為：

- **AR 相機頁**：`https://你的網址/ar-threex-camera.html`
- **首頁**：`https://你的網址/`（會打開 `index.html`）
- 相機參數與標記檔會自動從 `https://你的網址/data/data/camera_para.dat`、`patt.hiro` 載入。

---

## 方式二：用 GitHub 連動 Railway

1. 把專案推到 GitHub（需包含 `TestHtml`、`TestHtml/data/data/`、`server.js`、`package.json`、`railway.toml` 等）。
2. 到 [Railway](https://railway.app) → New Project → Deploy from GitHub repo，選這個專案。
3. 根目錄保留為專案根（不要設成 `TestHtml`），Railway 會依 `railway.toml` 執行 `npm start`，即會跑 `server.js` 並提供 TestHtml。
4. 部署完成後記下網址，同上用 `/ar-threex-camera.html` 與 `/` 訪問。

---

## 部署後請確認

1. **根目錄要正確**  
   Railway 的專案根必須是「有 `server.js` 和 `TestHtml` 的那一層」，不要設成 `TestHtml`，否則會沒有 Node 服務與靜態檔。

2. **TestHtml 要一併上傳**  
   - 若用 Git：請確認 `TestHtml/`、`TestHtml/data/data/` 有被 commit 並 push。  
   - 若用 `railway up`：在專案根執行，會打包整個目錄（含 TestHtml）。

3. **HTTPS 與攝影機**  
   手機或電腦用 `https://你的網址/ar-threex-camera.html` 開啟時，需允許攝影機權限；HTTPS 為必要條件。

4. **健康檢查**  
   可先開：`https://你的網址/health`，若回傳 JSON 表示服務正常，再開 AR 頁面。

---

## 本機先測一次（可選）

在專案根目錄執行：

```bash
npm start
```

瀏覽器打開：

- http://localhost:8080/ar-threex-camera.html  
- http://localhost:8080/data/data/camera_para.dat  

若都正常，部署到 Railway 後行為會一致（僅網址改為你的 Railway 網址）。

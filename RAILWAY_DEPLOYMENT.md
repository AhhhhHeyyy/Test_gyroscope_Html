# 🚀 Railway部署指南

## 📋 專案結構

```
Project1141/
├── package.json              # Node.js依賴配置
├── server.js                # WebSocket伺服器
├── railway.json             # Railway部署配置
├── Procfile                 # 進程配置
├── .gitignore               # Git忽略文件
├── README.md                # 專案說明
├── RAILWAY_DEPLOYMENT.md     # 本部署指南
└── Test_gyroscope_Html/
    └── TestHtml/
        ├── gyroscope.html    # 陀螺儀測試頁面
        ├── index.html        # 主頁
        └── ...
```

## 🔧 Railway部署步驟

### 1. 準備GitHub儲存庫
```bash
# 確保所有文件都在根目錄
git add .
git commit -m "Prepare for Railway deployment"
git push origin main
```

### 2. 在Railway上部署
1. 前往 [railway.app](https://railway.app)
2. 用GitHub帳戶登入
3. 點擊 **"New Project"**
4. 選擇 **"Deploy from GitHub repo"**
5. 選擇您的儲存庫：`AhhhhHeyyy/Test_gyroscope_Html`
6. Railway會自動偵測並部署

### 3. 配置環境變數（可選）
在Railway專案設定中，您可以設定：
- `NODE_ENV=production`
- `PORT=3000`（Railway會自動設定）

## 🔌 WebSocket連接

### 連接URL格式
- 開發環境: `ws://localhost:3000`
- 生產環境: `wss://your-project-name.up.railway.app`

### Unity端連接範例
```csharp
using WebSocketSharp;

public class GyroscopeReceiver : MonoBehaviour
{
    private WebSocket ws;
    private string serverUrl = "wss://your-project-name.up.railway.app";
    
    void Start()
    {
        ConnectToServer();
    }
    
    void ConnectToServer()
    {
        ws = new WebSocket(serverUrl);
        
        ws.OnOpen += (sender, e) =>
        {
            Debug.Log("WebSocket連接已建立");
        };
        
        ws.OnMessage += (sender, e) =>
        {
            try
            {
                var data = JsonUtility.FromJson<GyroscopeData>(e.Data);
                if (data.type == "gyroscope")
                {
                    ProcessGyroscopeData(data.data);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("解析數據錯誤: " + ex.Message);
            }
        };
        
        ws.Connect();
    }
    
    void ProcessGyroscopeData(GyroscopeData data)
    {
        // 處理陀螺儀數據
        transform.rotation = Quaternion.Euler(data.beta, data.alpha, data.gamma);
    }
}
```

## 📱 測試流程

### 1. 手機端測試
1. 在手機上開啟Railway部署的網址
2. 允許瀏覽器存取陀螺儀權限
3. 旋轉手機查看即時數據
4. 檢查WebSocket連接狀態

### 2. Unity端測試
1. 在Unity中建立WebSocket客戶端
2. 連接到Railway部署的網址
3. 接收並處理陀螺儀數據
4. 驗證數據傳輸的即時性

## 🛠️ 故障排除

### 常見問題

#### 1. 部署失敗
- 檢查 `package.json` 是否正確
- 確認 `server.js` 在根目錄
- 檢查GitHub儲存庫是否包含所有必要文件

#### 2. WebSocket連接失敗
- 確認使用正確的協議（`wss://` 用於HTTPS）
- 檢查防火牆設定
- 確認Railway服務正在運行

#### 3. 陀螺儀權限問題
- 確保在HTTPS環境下測試
- 檢查瀏覽器權限設定
- 確認設備支援陀螺儀

### 日誌檢查
在Railway控制台中查看：
- Build Logs: 建置過程
- Deploy Logs: 部署過程
- Application Logs: 運行時日誌

## 📊 監控和維護

### 健康檢查端點
- `/health`: 基本健康狀態
- `/api/status`: 詳細狀態資訊
- `/api/ping`: 保持活躍檢查

### 日誌監控
Railway提供即時日誌查看，可以監控：
- WebSocket連接數
- 數據傳輸頻率
- 錯誤訊息
- 記憶體使用量

## 🔄 更新部署

當您修改代碼後：
1. 提交變更到GitHub
2. Railway會自動重新部署
3. 檢查部署狀態和日誌

## 📞 支援

如果遇到問題：
1. 檢查Railway日誌
2. 確認代碼語法正確
3. 測試本地環境
4. 查看Railway文檔

---

**部署完成後，您就可以透過WebSocket實現手機陀螺儀與Unity的實時數據傳輸了！** 🎉

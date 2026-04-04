const { Client } = require('@notionhq/client');

const notion = new Client({ auth: process.env.NOTION_TOKEN });
const PAGE_ID = '32d8865d17608016a62cf2051afab12a';

const blocks = [
  {
    object: 'block', type: 'heading_1',
    heading_1: { rich_text: [{ type: 'text', text: { content: '2026-03-24 Debug：手機陀螺儀軸向對應問題' } }] }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '問題描述' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '直放手機：Unity 物件旋轉正常對應' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '平放手機：轉錯軸，原本該轉 Z 軸卻變成轉 Y 軸' } }] }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '根本原因：DeviceOrientation 的 Euler 角限制' } }] }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: 'DeviceOrientation API 提供 alpha/beta/gamma 三個 Euler 角。同一個物理動作（繞螢幕法線旋轉）在不同手機姿勢下對應到不同的 Euler 角：' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '直放手機：繞螢幕法線轉 → gamma 改變' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '平放手機：法線朝上（地球 Z 軸）→ 同樣動作改變的是 alpha' } }] }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: '所以 Unity 端無論怎麼組合 alpha/beta/gamma，都無法完全消除軸向混淆，必須從 JS 端直接送四元數。' } }] }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '錯誤路徑一：Quaternion.Euler()' } }] }
  },
  {
    object: 'block', type: 'code',
    code: {
      language: 'c#',
      rich_text: [{ type: 'text', text: { content: '// 原本的寫法（錯誤）\ntransform.rotation = Quaternion.Euler(\n    receiver.m_beta,   // 前後傾 → X\n    receiver.m_gamma,  // 左右傾 → Y\n    -receiver.m_alpha  // 方位 → Z\n);' } }]
    }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: '問題：Quaternion.Euler() 把三個角視為世界空間的獨立旋轉，但 DeviceOrientation 的每個角是在上一步旋轉後的新框架下定義的。平放時對應關係完全跑掉。' } }] }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '錯誤路徑二：Unity 端四元數連乘' } }] }
  },
  {
    object: 'block', type: 'code',
    code: {
      language: 'c#',
      rich_text: [{ type: 'text', text: { content: '// 改進版（仍有根本限制）\nQuaternion qCompass = Quaternion.AngleAxis(-alpha, Vector3.up);\nQuaternion qPitch   = Quaternion.AngleAxis( beta,  Vector3.right);\nQuaternion qRoll    = Quaternion.AngleAxis(-gamma, Vector3.forward);\ntransform.rotation  = qCompass * qPitch * qRoll;' } }]
    }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: '比 Quaternion.Euler() 正確（每步在新框架執行），但因為 source data（alpha/beta/gamma）本身就有軸向混淆，平放時仍然轉錯。另外 alpha 在 beta=±90° 時會突然跳 180°（萬向節死鎖），造成 Y 軸突然轉一圈。' } }] }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '正確解法：JS 端計算四元數 → 送給 Unity（Pipeline 方案）' } }] }
  },
  {
    object: 'block', type: 'heading_3',
    heading_3: { rich_text: [{ type: 'text', text: { content: '修改一：index.html - sendGyroscopeData 加入四元數計算' } }] }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: 'DeviceOrientation 的旋轉順序是 Rz(alpha) * Rx(beta) * Ry(gamma)，直接在 JS 展開成四元數公式：' } }] }
  },
  {
    object: 'block', type: 'code',
    code: {
      language: 'javascript',
      rich_text: [{ type: 'text', text: { content: 'const r = Math.PI / 180;\nconst a2 = (alpha||0)*r/2, b2 = (beta||0)*r/2, g2 = (gamma||0)*r/2;\nconst ca=Math.cos(a2), sa=Math.sin(a2);\nconst cb=Math.cos(b2), sb=Math.sin(b2);\nconst cg=Math.cos(g2), sg=Math.sin(g2);\nconst qw = ca*cb*cg - sa*sb*sg;\nconst qx = ca*sb*cg - sa*cb*sg;\nconst qy = ca*cb*sg + sa*sb*cg;\nconst qz = ca*sb*sg + sa*cb*cg;\n// 連同 alpha/beta/gamma 一起送出 {qx, qy, qz, qw}' } }]
    }
  },
  {
    object: 'block', type: 'heading_3',
    heading_3: { rich_text: [{ type: 'text', text: { content: '修改二：server.js - 轉發 qx/qy/qz/qw' } }] }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: '在 gyroscope 訊息的 data 物件中加入 qx: msg.qx, qy: msg.qy, qz: msg.qz, qw: msg.qw，確保傳到 Unity 端。注意：server.js 修改後必須重啟才生效（Node.js 不熱重載）。' } }] }
  },
  {
    object: 'block', type: 'heading_3',
    heading_3: { rich_text: [{ type: 'text', text: { content: '修改三：GyroscopeReceiver.cs - 加入四元數欄位' } }] }
  },
  {
    object: 'block', type: 'code',
    code: {
      language: 'c#',
      rich_text: [{ type: 'text', text: { content: '// GyroscopeData class 加入\npublic float qx;\npublic float qy;\npublic float qz;\npublic float qw;\n\n// Value 區域加入 public 欄位\npublic float m_qx = 0f;\npublic float m_qy = 0f;\npublic float m_qz = 0f;\npublic float m_qw = 1f;\n\n// 訊息接收處同步\nm_qx = gyroData.qx;\nm_qy = gyroData.qy;\nm_qz = gyroData.qz;\nm_qw = gyroData.qw;' } }]
    }
  },
  {
    object: 'block', type: 'heading_3',
    heading_3: { rich_text: [{ type: 'text', text: { content: '修改四：test.cs - 套用四元數並轉換座標系' } }] }
  },
  {
    object: 'block', type: 'paragraph',
    paragraph: { rich_text: [{ type: 'text', text: { content: 'Browser 為右手系（X=East, Y=North, Z=Up），Unity 為左手系（X=Right, Y=Up, Z=Forward）。axis 映射 (bx,by,bz)→(bx,bz,by)，det=-1（換手性），旋轉方向反向，xyz 全取負。微調後最終版：' } }] }
  },
  {
    object: 'block', type: 'code',
    code: {
      language: 'c#',
      rich_text: [{ type: 'text', text: { content: 'float qx = receiver.m_qx, qy = receiver.m_qy, qz = receiver.m_qz, qw = receiver.m_qw;\nfloat mag2 = qx*qx + qy*qy + qz*qz + qw*qw;\nif (mag2 < 0.5f) return;  // 四元數無效則跳過\n\n// Browser 右手系 → Unity 左手系座標轉換\n// 最終微調版（X/Z 方向對齊後）\ntransform.rotation = new Quaternion(qx, -qz, qy, qw);' } }]
    }
  },
  {
    object: 'block', type: 'heading_2',
    heading_2: { rich_text: [{ type: 'text', text: { content: '踩到的坑' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: 'server.js 改完沒重啟 → qw=0 → Unity 拿到 (0,0,0,0) 無效四元數 → 物件完全不動' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '對 qw 加了「==0 就設成 1」的保護，但 qw 合法可以為 0（180° 旋轉時），這個保護反而破壞四元數 → 已移除' } }] }
  },
  {
    object: 'block', type: 'bulleted_list_item',
    bulleted_list_item: { rich_text: [{ type: 'text', text: { content: '座標轉換公式 y 分量符號錯誤（+qz 應為 -qz），導致某個軸轉反 → 透過實際測試逐步修正符號' } }] }
  },
  {
    object: 'block', type: 'divider',
    divider: {}
  }
];

async function appendBlocksInBatches(pageId, blocks) {
  const BATCH_SIZE = 95;
  for (let i = 0; i < blocks.length; i += BATCH_SIZE) {
    const batch = blocks.slice(i, i + BATCH_SIZE);
    await notion.blocks.children.append({ block_id: pageId, children: batch });
    console.log(`  上傳 ${Math.min(i + BATCH_SIZE, blocks.length)} / ${blocks.length} 個區塊`);
  }
}

appendBlocksInBatches(PAGE_ID, blocks)
  .then(() => console.log('✅ 上傳完成'))
  .catch(err => {
    console.error('❌ 錯誤:', err.message);
    if (err.body) console.error(JSON.stringify(err.body, null, 2));
  });

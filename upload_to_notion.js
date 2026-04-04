const { Client } = require('@notionhq/client');
const fs = require('fs');

const notion = new Client({ auth: process.env.NOTION_TOKEN });
const PARENT_PAGE_ID = '3288865d176080c2929cc8acba2939cf';
const README_PATH = './README_GyroSystem.md';

// Convert markdown lines to Notion blocks (simplified parser)
function mdToBlocks(text) {
  const lines = text.split('\n');
  const blocks = [];

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];

    // Skip empty lines
    if (line.trim() === '') {
      continue;
    }

    // H1
    if (/^# (.+)/.test(line)) {
      const content = line.replace(/^# /, '');
      blocks.push({ object: 'block', type: 'heading_1', heading_1: { rich_text: [{ type: 'text', text: { content } }] } });
      continue;
    }

    // H2
    if (/^## (.+)/.test(line)) {
      const content = line.replace(/^## /, '');
      blocks.push({ object: 'block', type: 'heading_2', heading_2: { rich_text: [{ type: 'text', text: { content } }] } });
      continue;
    }

    // H3
    if (/^### (.+)/.test(line)) {
      const content = line.replace(/^### /, '');
      blocks.push({ object: 'block', type: 'heading_3', heading_3: { rich_text: [{ type: 'text', text: { content } }] } });
      continue;
    }

    // Horizontal rule
    if (/^---$/.test(line.trim())) {
      blocks.push({ object: 'block', type: 'divider', divider: {} });
      continue;
    }

    // Code block start/end
    if (/^```/.test(line)) {
      const lang = line.replace(/^```/, '').trim() || 'plain text';
      const codeLines = [];
      i++;
      while (i < lines.length && !/^```/.test(lines[i])) {
        codeLines.push(lines[i]);
        i++;
      }
      const code = codeLines.join('\n');
      blocks.push({
        object: 'block',
        type: 'code',
        code: {
          rich_text: [{ type: 'text', text: { content: code.substring(0, 2000) } }],
          language: lang === 'csharp' ? 'c#' : lang === 'bash' ? 'shell' : lang === 'json' ? 'json' : 'plain text'
        }
      });
      continue;
    }

    // Table (skip - too complex, output as paragraph)
    if (/^\|/.test(line)) {
      // Skip separator rows
      if (/^\|[\s\-\|]+\|$/.test(line)) continue;
      const content = line.replace(/\|/g, ' | ').replace(/\*\*(.+?)\*\*/g, '$1').trim();
      blocks.push({ object: 'block', type: 'paragraph', paragraph: { rich_text: [{ type: 'text', text: { content: content.substring(0, 2000) } }] } });
      continue;
    }

    // Bullet list
    if (/^[\-\*] (.+)/.test(line)) {
      const content = line.replace(/^[\-\*] /, '').replace(/\*\*(.+?)\*\*/g, '$1').replace(/`(.+?)`/g, '$1');
      blocks.push({ object: 'block', type: 'bulleted_list_item', bulleted_list_item: { rich_text: [{ type: 'text', text: { content: content.substring(0, 2000) } }] } });
      continue;
    }

    // Numbered list
    if (/^\d+\. (.+)/.test(line)) {
      const content = line.replace(/^\d+\. /, '').replace(/\*\*(.+?)\*\*/g, '$1').replace(/`(.+?)`/g, '$1');
      blocks.push({ object: 'block', type: 'numbered_list_item', numbered_list_item: { rich_text: [{ type: 'text', text: { content: content.substring(0, 2000) } }] } });
      continue;
    }

    // Blockquote
    if (/^> (.+)/.test(line)) {
      const content = line.replace(/^> /, '').replace(/⚠️ /g, '');
      blocks.push({ object: 'block', type: 'quote', quote: { rich_text: [{ type: 'text', text: { content: content.substring(0, 2000) } }] } });
      continue;
    }

    // Regular paragraph (strip markdown formatting)
    const content = line.replace(/\*\*(.+?)\*\*/g, '$1').replace(/`(.+?)`/g, '$1').replace(/\[(.+?)\]\(.+?\)/g, '$1');
    if (content.trim()) {
      blocks.push({ object: 'block', type: 'paragraph', paragraph: { rich_text: [{ type: 'text', text: { content: content.substring(0, 2000) } }] } });
    }
  }

  return blocks;
}

async function appendBlocksInBatches(pageId, blocks) {
  const BATCH_SIZE = 95;
  for (let i = 0; i < blocks.length; i += BATCH_SIZE) {
    const batch = blocks.slice(i, i + BATCH_SIZE);
    await notion.blocks.children.append({ block_id: pageId, children: batch });
    console.log(`  已上傳 ${Math.min(i + BATCH_SIZE, blocks.length)} / ${blocks.length} 個區塊`);
  }
}

async function main() {
  const md = fs.readFileSync(README_PATH, 'utf8');

  // Extract title from first H1
  const titleMatch = md.match(/^# (.+)/m);
  const title = titleMatch ? titleMatch[1] : '手機感測器 × WebSocket × Unity 互動系統';

  console.log('📄 建立 Notion 頁面...');
  const page = await notion.pages.create({
    parent: { page_id: PARENT_PAGE_ID },
    properties: {
      title: { title: [{ text: { content: title } }] }
    }
  });

  console.log(`✅ 頁面已建立: ${page.url}`);
  console.log('📦 轉換 Markdown → Notion 區塊...');

  const blocks = mdToBlocks(md);
  console.log(`  共 ${blocks.length} 個區塊，開始批次上傳...`);

  await appendBlocksInBatches(page.id, blocks);

  console.log(`\n🎉 上傳完成！`);
  console.log(`🔗 頁面連結: ${page.url}`);
}

main().catch(err => {
  console.error('❌ 錯誤:', err.message);
  if (err.body) console.error('API 回應:', JSON.stringify(err.body, null, 2));
  process.exit(1);
});

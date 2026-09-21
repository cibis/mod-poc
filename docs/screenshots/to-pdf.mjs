import { chromium } from 'playwright';
import { readFileSync } from 'fs';
import { resolve } from 'path';

const MD_PATH = resolve('../../tmp/ui-testing-guide.md');
const OUT_PATH = resolve('../../tmp/ui-testing-guide.pdf');

const md = readFileSync(MD_PATH, 'utf8');

// Expand markdown links: [text](url) → text — url  (when text != url)
//                        [url](url)  → url
function expandLinks(text) {
  return text.replace(/\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g, (_, label, url) => {
    if (label.trim() === url.trim()) return url;
    return `${label} — ${url}`;
  });
}

const expanded = expandLinks(md);

// Minimal markdown → HTML (handles the constructs in this document)
function mdToHtml(src) {
  const lines = src.split('\n');
  const out = [];
  let inCode = false;
  let inTable = false;
  let inList = false;

  for (let i = 0; i < lines.length; i++) {
    let line = lines[i];

    // Fenced code block
    if (line.startsWith('```')) {
      if (!inCode) { out.push('<pre><code>'); inCode = true; }
      else { out.push('</code></pre>'); inCode = false; }
      continue;
    }
    if (inCode) { out.push(escHtml(line)); continue; }

    // Close list if not a list line
    if (inList && !line.match(/^[-*] |\d+\. /)) {
      out.push('</ul>');
      inList = false;
    }

    // Close table if blank or non-table line
    if (inTable && !line.startsWith('|')) {
      out.push('</tbody></table>');
      inTable = false;
    }

    // Headings
    if (line.startsWith('#### ')) { out.push(`<h4>${inline(line.slice(5))}</h4>`); continue; }
    if (line.startsWith('### '))  { out.push(`<h3>${inline(line.slice(4))}</h3>`); continue; }
    if (line.startsWith('## '))   { out.push(`<h2>${inline(line.slice(3))}</h2>`); continue; }
    if (line.startsWith('# '))    { out.push(`<h1>${inline(line.slice(2))}</h1>`); continue; }

    // HR
    if (line.match(/^-{3,}$|^\*{3,}$|^_{3,}$/)) { out.push('<hr>'); continue; }

    // Blockquote
    if (line.startsWith('> ')) { out.push(`<blockquote>${inline(line.slice(2))}</blockquote>`); continue; }

    // Table row
    if (line.startsWith('|')) {
      // Skip separator rows
      if (line.match(/^\|[-| :]+\|$/)) continue;
      const cells = line.split('|').slice(1, -1).map(c => c.trim());
      if (!inTable) {
        out.push('<table><thead><tr>' + cells.map(c => `<th>${inline(c)}</th>`).join('') + '</tr></thead><tbody>');
        inTable = true;
        // Peek at next — if it's a separator, consume it
        if (lines[i + 1] && lines[i + 1].match(/^\|[-| :]+\|$/)) i++;
      } else {
        out.push('<tr>' + cells.map(c => `<td>${inline(c)}</td>`).join('') + '</tr>');
      }
      continue;
    }

    // Unordered list
    if (line.match(/^[-*] /)) {
      if (!inList) { out.push('<ul>'); inList = true; }
      out.push(`<li>${inline(line.replace(/^[-*] /, ''))}</li>`);
      continue;
    }
    // Ordered list
    if (line.match(/^\d+\. /)) {
      if (!inList) { out.push('<ol>'); inList = true; }
      out.push(`<li>${inline(line.replace(/^\d+\. /, ''))}</li>`);
      continue;
    }

    // Blank
    if (line.trim() === '') { out.push('<p></p>'); continue; }

    // Paragraph
    out.push(`<p>${inline(line)}</p>`);
  }

  if (inCode)  out.push('</code></pre>');
  if (inList)  out.push('</ul>');
  if (inTable) out.push('</tbody></table>');

  return out.join('\n');
}

function escHtml(s) {
  return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
}

function inline(s) {
  // Bold
  s = s.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
  // Italic / em
  s = s.replace(/_(.+?)_/g, '<em>$1</em>');
  // Inline code
  s = s.replace(/`([^`]+)`/g, '<code>$1</code>');
  // HTML escape (no < > in this doc's inline text)
  return s;
}

const body = mdToHtml(expanded);

const html = `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  * { box-sizing: border-box; }
  body {
    font-family: 'Segoe UI', Arial, sans-serif;
    font-size: 11pt;
    line-height: 1.55;
    color: #1a1a1a;
    margin: 0;
    padding: 0;
  }
  h1 { font-size: 20pt; margin: 0 0 6pt; border-bottom: 2px solid #1976D2; padding-bottom: 4pt; color: #1976D2; }
  h2 { font-size: 14pt; margin: 18pt 0 4pt; border-bottom: 1px solid #ccc; padding-bottom: 2pt; color: #1976D2; }
  h3 { font-size: 12pt; margin: 12pt 0 3pt; color: #333; }
  h4 { font-size: 11pt; margin: 10pt 0 2pt; color: #444; }
  p  { margin: 4pt 0; }
  blockquote {
    margin: 6pt 0 6pt 16pt;
    padding: 4pt 10pt;
    border-left: 3px solid #1976D2;
    background: #f0f4ff;
    color: #333;
    font-size: 10pt;
  }
  pre {
    background: #f5f5f5;
    border: 1px solid #ddd;
    border-radius: 4px;
    padding: 8pt 10pt;
    font-family: Consolas, 'Courier New', monospace;
    font-size: 9pt;
    overflow-wrap: break-word;
    white-space: pre-wrap;
    margin: 6pt 0;
  }
  code { font-family: Consolas, 'Courier New', monospace; font-size: 9.5pt; background: #f0f0f0; padding: 0 2pt; border-radius: 2px; }
  pre code { background: none; padding: 0; font-size: 9pt; }
  table {
    width: 100%;
    border-collapse: collapse;
    margin: 8pt 0;
    font-size: 10pt;
  }
  th {
    background: #1976D2;
    color: white;
    text-align: left;
    padding: 5pt 8pt;
    font-weight: 600;
  }
  td { padding: 4pt 8pt; border-bottom: 1px solid #e0e0e0; vertical-align: top; }
  tr:nth-child(even) td { background: #f8f8f8; }
  ul, ol { margin: 4pt 0 4pt 20pt; padding: 0; }
  li { margin: 2pt 0; }
  hr { border: none; border-top: 1px solid #ccc; margin: 14pt 0; }
  em { font-style: italic; color: #555; }
  strong { font-weight: 700; }
</style>
</head>
<body>
${body}
</body>
</html>`;

(async () => {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.setContent(html, { waitUntil: 'load' });
  await page.pdf({
    path: OUT_PATH,
    format: 'A4',
    margin: { top: '18mm', right: '15mm', bottom: '18mm', left: '15mm' },
    printBackground: true,
  });
  await browser.close();
  console.log('PDF written to', OUT_PATH);
})();

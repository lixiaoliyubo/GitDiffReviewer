window.addEventListener('error', (e) => {
  document.title = 'ERR: ' + e.message;
  const d = document.createElement('div');
  d.style.cssText = 'position:fixed;top:0;left:0;right:0;background:red;color:white;padding:8px;z-index:9999;font-size:12px;';
  d.textContent = e.message + ' @ ' + e.filename + ':' + e.lineno;
  document.body.appendChild(d);
});
const { createApp, ref, computed, onMounted, nextTick } = Vue;
const { ElMessage, ElMessageBox } = ElementPlus;

createApp({
  setup() {
    // ---- state ----
    const repos = ref([]);
    const activeRepo = ref('');
    const current = ref(null);
    const allFiles = ref([]);
    const searchQ = ref('');
    const filter = ref('all');
    const viewMode = ref('tree');
    const showAllFiles = ref(false);
    const inlineDiff = ref(true);
    const rightTab = ref('diff');
    const lastEntry = ref(null);
    const currentData = ref(null);
    const expandedDirs = ref(new Set());
    const editContent = ref('');
    const originalContent = ref('');
    const errorBar = ref('');
    const findPanelVisible = ref(false);
    const findQ = ref('');
    const replaceQ = ref('');
    const replaceRowVisible = ref(false);
    const findMatches = ref([]);
    const findIdx = ref(0);
    const autoCompleteVisible = ref(false);
    const autoCompleteItems = ref([]);
    const autoCompleteIndex = ref(0);
    const autoCompletePrefix = ref('');
    const autoCompletePos = ref({top:0,left:0});
    const diffHostEl = ref(null);
    const editorEl = ref(null);
    const minimapEl = ref(null);
    const minimapViewportTop = ref('0px');
    const savedScrollTop = ref(0);
    const savedScrollLeft = ref(0);
    const commitPanelOpen = ref(false);
    const commits = ref([]);
    const commitMode = ref(false);
    const currentCommitSha = ref('');
    const currentCommitShort = ref('');
    const currentCommitMessage = ref('');
    const currentCommitAuthor = ref('');
    const currentCommitDate = ref('');
    const commitInfoOpen = ref(true);
    const ignoredPaths = ref(new Set());
    const showIgnored = ref(false);
    const openTabs = ref([]);

    // ---- bridge ----
    let msgId = 0;
    const pending = new Map();
    function send(type, payload) {
      return new Promise((resolve, reject) => {
        const id = String(++msgId);
        pending.set(id, { resolve, reject });
        window.chrome.webview.postMessage({ id, type, payload: payload || {} });
      });
    }
    window.chrome.webview.addEventListener('message', (e) => {
      const msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
      if (msg.replyTo && pending.has(msg.replyTo)) {
        const { resolve, reject } = pending.get(msg.replyTo);
        pending.delete(msg.replyTo);
        if (msg.ok) resolve(msg.data);
        else reject(new Error(msg.error?.message || 'error'));
      }
    });

    // ---- helpers ----
    function esc(s) {
      return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
    }
    function fileExt(p) {
      const parts = p.split('/').pop().split('.');
      return parts.length > 1 ? parts.pop().toLowerCase() : '';
    }
    function extToLang(ext) {
      const m = {js:'javascript',ts:'typescript',jsx:'javascript',tsx:'typescript',vue:'xml',cs:'csharp',py:'python',css:'css',scss:'css',html:'xml',xml:'xml',json:'json',md:'markdown',java:'java',c:'c',h:'c',cpp:'cpp',go:'go',rs:'rust',php:'php',rb:'ruby',sh:'bash',sql:'sql',yml:'yaml',yaml:'yaml',dockerfile:'dockerfile'};
      return m[ext] || '';
    }
    function fileIcon(name) {
      const ext = name.split('.').pop().toLowerCase();
      const map = {js:'🟨',ts:'🔷',jsx:'🟦',tsx:'🔷',vue:'🟩',cs:'💜',py:'🐍',css:'🎨',scss:'🎨',html:'🌐',xml:'🌐',json:'📋',md:'📝',java:'☕',go:'🐹',rs:'🦀',php:'🐘',c:'⚙️',cpp:'⚙️',sh:'📜',sql:'🗃️',yml:'⚙️',yaml:'⚙️',lock:'🔒'};
      return map[ext] || '📄';
    }
    function dirOf(p) { const i = p.lastIndexOf('/'); return i >= 0 ? p.slice(0, i+1) : ''; }
    function baseOf(p) { const i = p.lastIndexOf('/'); return i >= 0 ? p.slice(i+1) : p; }
    function highlightCode(code, ext) {
      if (window.hljs) {
        const lang = extToLang(ext);
        if (lang) { try { return hljs.highlight(code, { language: lang, ignoreIllegals: true }).value; } catch(e){} }
      }
      return esc(code);
    }

    // ---- computed ----
    const visibleEntries = computed(() => {
      if (!current.value) return [];
      let entries = [...current.value.entries];
      if (showAllFiles.value && allFiles.value.length) {
        const changed = new Set(current.value.entries.map(e => e.path));
        const unchanged = allFiles.value.filter(p => !changed.has(p)).map(p => ({ path: p, status: 'unchanged', insertions: 0, deletions: 0 }));
        entries = [...entries, ...unchanged];
      }
      return entries.filter(e => {
        if (filter.value !== 'all' && e.status !== filter.value) return false;
        if (searchQ.value && !e.path.toLowerCase().includes(searchQ.value.toLowerCase())) return false;
        if (!showIgnored.value) {
          for (const ig of ignoredPaths.value) {
            if (e.path === ig || e.path.startsWith(ig + '/')) return false;
          }
        }
        return true;
      });
    });

    const treeData = computed(() => {
      const entries = visibleEntries.value;
      if (!entries.length) return [];
      if (viewMode.value === 'flat') {
        return entries.sort((a,b) => a.path.localeCompare(b.path)).map(e => ({
          path: e.path, name: e.path.split('/').pop(), children: null,
          insertions: e.insertions, deletions: e.deletions, status: e.status
        }));
      }
      // build tree
      const root = { name: '', path: '', children: new Map(), entries: [] };
      entries.forEach(e => {
        const parts = e.path.split('/');
        let node = root;
        let acc = '';
        for (let i = 0; i < parts.length - 1; i++) {
          const seg = parts[i];
          acc = acc ? acc + '/' + seg : seg;
          if (!node.children.has(seg)) node.children.set(seg, { name: seg, path: acc, children: new Map(), entries: [] });
          node = node.children.get(seg);
        }
        node.entries.push(e);
      });
      // flatten with expand/collapse
      const result = [];
      function walk(node, depth) {
        const dirs = [...node.children.entries()].sort((a,b) => a[0].localeCompare(b[0]));
        for (const [name, child] of dirs) {
          const agg = aggregate(child);
          result.push({
            path: child.path, name, children: true, _count: agg.files,
            hasModify: agg.hasModify, hasAdd: agg.hasAdd, depth
          });
          if (expandedDirs.value.has(child.path)) walk(child, depth + 1);
        }
        const files = node.entries.sort((a,b) => a.path.localeCompare(b.path));
        for (const f of files) {
          result.push({ path: f.path, name: f.path.split('/').pop(), children: null, insertions: f.insertions, deletions: f.deletions, status: f.status, depth: depth + 1 });
        }
      }
      function aggregate(node) {
        let files = node.entries.length, hasModify = false, hasAdd = false;
        for (const e of node.entries) {
          if (e.status === 'modified' || e.status === 'deleted' || e.status === 'renamed') hasModify = true;
          if (e.status === 'added') hasAdd = true;
        }
        for (const child of node.children.values()) {
          const a = aggregate(child);
          files += a.files;
          if (a.hasModify) hasModify = true;
          if (a.hasAdd) hasAdd = true;
        }
        return { files, hasModify, hasAdd };
      }
      walk(root, 0);
      return result;
    });

    const statsText = computed(() => {
      if (!current.value) return '';
      let adds = 0, dels = 0, n = 0;
      for (const e of current.value.entries) { adds += e.insertions; dels += e.deletions; n++; }
      return `${n}  +${adds} -${dels}`;
    });

    const diffHtml = computed(() => {
      if (!currentData.value || !lastEntry.value) return '';
      const d = currentData.value;
      if (d.binary) return '<div class="placeholder">二进制文件</div>';
      const { html } = renderDiffHtml(d.oldText || '', d.newText || '');
      return html;
    });

    const minimapHtml = computed(() => {
      if (!currentData.value || !lastEntry.value) return '';
      const d = currentData.value;
      if (d.binary) return '';
      const { ops } = renderDiffHtml(d.oldText || '', d.newText || '');
      let s = '';
      for (const op of ops) {
        if (op.t === 'add') {
          if (!op.n || !op.n.trim()) continue; // 空行不显示红/绿
          s += `<div class="mm-line mm-add"></div>`;
        } else if (op.t === 'del') {
          if (!op.o || !op.o.trim()) continue;
          s += `<div class="mm-line mm-del"></div>`;
        } else {
          s += `<div class="mm-line mm-ctx"></div>`;
        }
      }
      return s;
    });

    const infoHtml = computed(() => {
      if (!currentData.value || !lastEntry.value) return '';
      const text = (currentData.value.newText || '').replace(/\r\n/g, '\n');
      const ext = fileExt(lastEntry.value.path);
      const highlighted = highlightCode(text, ext);
      return '<table class="diff plain">' + highlighted.split('\n').map((l,i) => `<tr><td class="ln">${i+1}</td><td class="code">${l||' '}</td></tr>`).join('') + '</table>';
    });

    const highlightedContent = computed(() => {
      if (!editContent.value) return '';
      const ext = lastEntry.value ? fileExt(lastEntry.value.path) : '';
      const lang = extToLang(ext);
      if (window.hljs && lang) {
        try { return hljs.highlight(editContent.value, { language: lang, ignoreIllegals: true }).value; } catch(e){}
      }
      return esc(editContent.value);
    });

    const lineNumbers = computed(() => {
      const n = editContent.value.split('\n').length;
      let s = '';
      for (let i = 1; i <= n; i++) s += i + '\n';
      return s;
    });

    function syncScroll(e) {
      const area = e.target.closest('.editor-area');
      if (!area) return;
      const bg = area.querySelector('.highlight-bg');
      if (bg) bg.style.transform = `translate(${-e.target.scrollLeft}px, ${-e.target.scrollTop}px)`;
      const gutter = e.target.closest('.code-editor').querySelector('.gutter-inner');
      if (gutter) gutter.style.transform = `translateY(${-e.target.scrollTop}px)`;
      savedScrollTop.value = e.target.scrollTop;
      savedScrollLeft.value = e.target.scrollLeft;
    }

    // ---- code snippets / autocomplete ----
    const SNIPPETS = {
      vue: [
        { k:'template', label:'<template>', desc:'Vue 模板根节点', body:'<template>\n  $0\n</template>' },
        { k:'script', label:'<script setup>', desc:'组合式 API', body:'<script setup lang="ts">\n$0\n</script>' },
        { k:'style', label:'<style scoped>', desc:'组件样式', body:'<style scoped>\n$0\n</style>' },
        { k:'ref', label:'ref()', desc:'响应式引用', body:'ref($0)' },
        { k:'computed', label:'computed()', desc:'计算属性', body:'computed(() => {\n  $0\n})' },
        { k:'watch', label:'watch()', desc:'侦听器', body:'watch(() => $0, (v) => {\n})' },
        { k:'watchEffect', label:'watchEffect()', desc:'立即执行侦听', body:'watchEffect(() => {\n  $0\n})' },
        { k:'onMounted', label:'onMounted()', desc:'挂载生命周期', body:'onMounted(() => {\n  $0\n})' },
        { k:'onUnmounted', label:'onUnmounted()', desc:'卸载生命周期', body:'onUnmounted(() => {\n  $0\n})' },
        { k:'props', label:'defineProps()', desc:'组件 props', body:'defineProps<{\n  $0\n}>()' },
        { k:'emit', label:'defineEmits()', desc:'组件事件', body:'defineEmits<{\n  $0\n}>()' },
      ],
      ts: [
        { k:'if', label:'if', desc:'条件判断', body:'if ($0) {\n  \n}' },
        { k:'else', label:'else', desc:'否则', body:' else {\n  $0\n}' },
        { k:'elif', label:'else if', desc:'否则如果', body:' else if ($0) {\n  \n}' },
        { k:'for', label:'for', desc:'for 循环', body:'for (let i = 0; i < $0; i++) {\n  \n}' },
        { k:'forof', label:'for...of', desc:'遍历数组', body:'for (const item of $0) {\n  \n}' },
        { k:'forin', label:'for...in', desc:'遍历对象键', body:'for (const key in $0) {\n  \n}' },
        { k:'foreach', label:'forEach', desc:'数组遍历', body:'.forEach((item) => {\n  $0\n})' },
        { k:'map', label:'map', desc:'数组映射', body:'.map((item) => {\n  return $0\n})' },
        { k:'filter', label:'filter', desc:'过滤数组', body:'.filter((item) => $0)' },
        { k:'find', label:'find', desc:'查找元素', body:'.find((item) => $0)' },
        { k:'reduce', label:'reduce', desc:'累加', body:'.reduce((acc, item) => {\n  return acc + $0\n}, 0)' },
        { k:'func', label:'function', desc:'函数声明', body:'function $0() {\n  \n}' },
        { k:'arrow', label:'() => {}', desc:'箭头函数', body:'() => {\n  $0\n}' },
        { k:'const', label:'const', desc:'常量', body:'const $0 = ' },
        { k:'let', label:'let', desc:'变量', body:'let $0 = ' },
        { k:'return', label:'return', desc:'返回', body:'return $0' },
        { k:'interface', label:'interface', desc:'接口', body:'interface $0 {\n  \n}' },
        { k:'type', label:'type', desc:'类型别名', body:'type $0 = ' },
        { k:'enum', label:'enum', desc:'枚举', body:'enum $0 {\n  \n}' },
        { k:'async', label:'async function', desc:'异步函数', body:'async function $0() {\n  \n}' },
        { k:'await', label:'await', desc:'等待 Promise', body:'await $0' },
        { k:'promise', label:'new Promise', desc:'Promise 构造', body:'new Promise((resolve, reject) => {\n  $0\n})' },
        { k:'then', label:'.then()', desc:'Promise 链', body:'.then((res) => {\n  $0\n})' },
        { k:'try', label:'try/catch', desc:'异常处理', body:'try {\n  $0\n} catch (e) {\n  \n}' },
        { k:'class', label:'class', desc:'类', body:'class $0 {\n  \n}' },
        { k:'constructor', label:'constructor', desc:'构造函数', body:'constructor() {\n  super()\n  $0\n}' },
        { k:'import', label:'import', desc:'导入模块', body:'import $0 from "' },
        { k:'export', label:'export default', desc:'默认导出', body:'export default $0' },
        { k:'settimeout', label:'setTimeout', desc:'延时执行', body:'setTimeout(() => {\n  $0\n}, 1000)' },
        { k:'interval', label:'setInterval', desc:'定时执行', body:'setInterval(() => {\n  $0\n}, 1000)' },
        { k:'json', label:'JSON.stringify', desc:'序列化', body:'JSON.stringify($0, null, 2)' },
        { k:'log', label:'console.log', desc:'打印日志', body:'console.log($0)' },
        { k:'warn', label:'console.warn', desc:'警告', body:'console.warn($0)' },
        { k:'error', label:'console.error', desc:'错误', body:'console.error($0)' },
        { k:'fetch', label:'fetch()', desc:'网络请求', body:'fetch("$0")\n  .then(res => res.json())\n  .then(data => {})' },
      ],
      css: [
        { k:'flex', label:'display:flex', desc:'弹性布局居中', body:'display: flex;\n  justify-content: center;\n  align-items: center;' },
        { k:'flexcol', label:'flex-column', desc:'纵向弹性', body:'display: flex;\n  flex-direction: column;' },
        { k:'flexrow', label:'flex-row', desc:'横向弹性', body:'display: flex;\n  flex-direction: row;' },
        { k:'flexwrap', label:'flex-wrap', desc:'自动换行', body:'display: flex;\n  flex-wrap: wrap;' },
        { k:'grid', label:'display:grid', desc:'网格布局', body:'display: grid;\n  grid-template-columns: repeat(3, 1fr);\n  gap: 8px;' },
        { k:'media', label:'@media', desc:'媒体查询', body:'@media ($0) {\n  \n}' },
        { k:'pos', label:'position:absolute', desc:'绝对定位', body:'position: absolute;\n  top: 0; left: 0;' },
        { k:'abs', label:'absolute', desc:'绝对定位值', body:'absolute' },
        { k:'rel', label:'relative', desc:'相对定位', body:'relative' },
        { k:'fixed', label:'fixed', desc:'固定定位', body:'position: fixed;' },
        { k:'sticky', label:'sticky', desc:'粘性定位', body:'position: sticky;\n  top: 0;' },
        { k:'col', label:'color', desc:'文字颜色', body:'color: $0;' },
        { k:'bg', label:'background', desc:'背景', body:'background: $0;' },
        { k:'bgc', label:'background-color', desc:'背景色', body:'background-color: $0;' },
        { k:'fs', label:'font-size', desc:'字号', body:'font-size: $0px;' },
        { k:'fw', label:'font-weight', desc:'字重', body:'font-weight: $0;' },
        { k:'ff', label:'font-family', desc:'字体', body:'font-family: $0;' },
        { k:'lh', label:'line-height', desc:'行高', body:'line-height: $0;' },
        { k:'ta', label:'text-align', desc:'对齐', body:'text-align: $0;' },
        { k:'td', label:'text-decoration', desc:'文字装饰', body:'text-decoration: $0;' },
        { k:'pd', label:'padding', desc:'内边距', body:'padding: $0;' },
        { k:'pt', label:'padding-top', body:'padding-top: $0;' },
        { k:'pb', label:'padding-bottom', body:'padding-bottom: $0;' },
        { k:'pl', label:'padding-left', body:'padding-left: $0;' },
        { k:'pr', label:'padding-right', body:'padding-right: $0;' },
        { k:'mg', label:'margin', desc:'外边距', body:'margin: $0;' },
        { k:'mt', label:'margin-top', body:'margin-top: $0;' },
        { k:'mb', label:'margin-bottom', body:'margin-bottom: $0;' },
        { k:'ml', label:'margin-left', body:'margin-left: $0;' },
        { k:'mr', label:'margin-right', body:'margin-right: $0;' },
        { k:'mga', label:'margin:auto', desc:'水平居中', body:'margin: 0 auto;' },
        { k:'w', label:'width', desc:'宽度', body:'width: $0;' },
        { k:'h', label:'height', desc:'高度', body:'height: $0;' },
        { k:'mw', label:'min-width', desc:'最小宽度', body:'min-width: $0;' },
        { k:'mh', label:'min-height', desc:'最小高度', body:'min-height: $0;' },
        { k:'bw', label:'border-width', desc:'边框宽度', body:'border: 1px solid $0;' },
        { k:'br', label:'border-radius', desc:'圆角', body:'border-radius: $0px;' },
        { k:'bd', label:'border', desc:'边框', body:'border: 1px solid $0;' },
        { k:'out', label:'outline', desc:'轮廓', body:'outline: none;' },
        { k:'shadow', label:'box-shadow', desc:'阴影', body:'box-shadow: 0 2px 8px rgba(0,0,0,0.15);' },
        { k:'opacity', label:'opacity', desc:'透明度', body:'opacity: $0;' },
        { k:'cursor', label:'cursor', desc:'鼠标样式', body:'cursor: pointer;' },
        { k:'display', label:'display', desc:'显示方式', body:'display: $0;' },
        { k:'hidden', label:'display:none', desc:'隐藏', body:'display: none;' },
        { k:'block', label:'display:block', desc:'块级', body:'display: block;' },
        { k:'inline', label:'display:inline-block', desc:'行内块', body:'display: inline-block;' },
        { k:'overflow', label:'overflow', desc:'溢出', body:'overflow: $0;' },
        { k:'ox', label:'overflow-x', body:'overflow-x: auto;' },
        { k:'oy', label:'overflow-y', body:'overflow-y: auto;' },
        { k:'tr', label:'transition', desc:'过渡', body:'transition: all 0.3s ease;' },
        { k:'trall', label:'transition all', desc:'全部过渡', body:'transition: all 0.2s ease;' },
        { k:'transform', label:'transform', desc:'变换', body:'transform: $0;' },
        { k:'translate', label:'translate', desc:'平移', body:'transform: translate($0, 0);' },
        { k:'scale', label:'scale', desc:'缩放', body:'transform: scale($0);' },
        { k:'rotate', label:'rotate', desc:'旋转', body:'transform: rotate($0deg);' },
        { k:'gapo', label:'gap', desc:'间距', body:'gap: $0px;' },
        { k:'posrel', label:'position:relative', desc:'相对定位容器', body:'position: relative;' },
        { k:'z', label:'z-index', desc:'层级', body:'z-index: $0;' },
        { k:'white', label:'white-space', desc:'空白处理', body:'white-space: nowrap;' },
        { k:'ellipsis', label:'text-overflow', desc:'省略号', body:'overflow: hidden;\n  text-overflow: ellipsis;\n  white-space: nowrap;' },
        { k:'userselect', label:'user-select', desc:'禁止选中', body:'user-select: none;' },
        { k:'boxsizing', label:'box-sizing', desc:'盒模型', body:'box-sizing: border-box;' },
      ],
      html: [
        { k:'div', label:'<div>', desc:'div 容器', body:'<div class="$0">\n  \n</div>' },
        { k:'span', label:'<span>', body:'<span>$0</span>' },
        { k:'img', label:'<img>', body:'<img src="$0" alt="">' },
        { k:'input', label:'<input>', body:'<input type="text" v-model="$0">' },
        { k:'button', label:'<button>', body:'<button @click="$0"></button>' },
        { k:'ul', label:'<ul><li>', body:'<ul>\n  <li>$0</li>\n</ul>' },
      ],
    };

    function getSnippets() {
      const ext = lastEntry.value ? fileExt(lastEntry.value.path) : '';
      if (ext === 'vue') return [...SNIPPETS.vue, ...SNIPPETS.ts, ...SNIPPETS.css, ...SNIPPETS.html];
      if (ext === 'ts' || ext === 'tsx' || ext === 'js' || ext === 'jsx') return SNIPPETS.ts;
      if (ext === 'css' || ext === 'scss' || ext === 'less') return SNIPPETS.css;
      if (ext === 'html' || ext === 'xml') return SNIPPETS.html;
      return [...SNIPPETS.ts, ...SNIPPETS.vue];
    }

    function onEditorInput(e) {
      syncScroll(e);
      const ta = e.target;
      const pos = ta.selectionStart;
      const text = ta.value.slice(0, pos);
      const m = text.match(/([a-zA-Z_$][a-zA-Z0-9_$-]*)$/);
      if (!m) { autoCompleteVisible.value = false; return; }
      const prefix = m[1].toLowerCase();
      if (prefix.length < 1) { autoCompleteVisible.value = false; return; }
      const snips = getSnippets().filter(s => s.k.toLowerCase().startsWith(prefix));
      if (snips.length) {
        autoCompleteItems.value = snips.slice(0, 10);
        autoCompleteIndex.value = 0;
        autoCompletePrefix.value = m[1];
        // 计算光标位置（考虑滚动偏移）
        const rect = ta.getBoundingClientRect();
        const lineHeight = 19;
        const charWidth = 7.5;
        const beforeText = ta.value.slice(0, pos);
        const lineNum = beforeText.split('\n').length - 1;
        const lineStart = beforeText.lastIndexOf('\n') + 1;
        const col = pos - lineStart;
        const scrolledLines = ta.scrollTop / lineHeight;
        const visibleLine = lineNum - scrolledLines;
        autoCompletePos.value = {
          top: rect.top + 12 + (visibleLine * lineHeight),
          left: rect.left + 12 + (col * charWidth)
        };
        autoCompleteVisible.value = true;
      } else {
        autoCompleteVisible.value = false;
      }
    }

    function applySnippet(snip) {
      const ta = editorEl.value;
      if (!ta) return;
      const pos = ta.selectionStart;
      const prefixLen = autoCompletePrefix.value.length;
      const before = ta.value.slice(0, pos - prefixLen);
      const after = ta.value.slice(pos);
      const body = snip.body;
      const cursorPos = body.indexOf('$0');
      const finalBody = body.replace('$0', '');
      ta.value = before + finalBody + after;
      editContent.value = ta.value;
      const newCursor = before.length + cursorPos;
      ta.selectionStart = ta.selectionEnd = newCursor;
      autoCompleteVisible.value = false;
      ta.focus();
    }

    function onEditorKeydown(e) {
      if (!autoCompleteVisible.value) {
        if (e.key === 'Tab') { e.preventDefault(); }
        return;
      }
      if (e.key === 'ArrowDown') {
        e.preventDefault();
        autoCompleteIndex.value = (autoCompleteIndex.value + 1) % autoCompleteItems.value.length;
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        autoCompleteIndex.value = (autoCompleteIndex.value - 1 + autoCompleteItems.value.length) % autoCompleteItems.value.length;
      } else if (e.key === 'Tab' || e.key === 'Enter') {
        e.preventDefault();
        applySnippet(autoCompleteItems.value[autoCompleteIndex.value]);
      } else if (e.key === 'Escape') {
        autoCompleteVisible.value = false;
      }
    }

    function updateMinimapPosition() {
      const host = diffHostEl.value;
      const mm = minimapEl.value;
      if (!host || !mm) return;
      const ratio = host.scrollHeight > host.clientHeight
        ? host.scrollTop / (host.scrollHeight - host.clientHeight) : 0;
      const mmH = mm.clientHeight;
      // viewport 高度30px，在容器内上下移动
      minimapViewportTop.value = Math.max(0, Math.min(mmH - 30, ratio * (mmH - 30))) + 'px';
    }

    function onDiffScroll(e) {
      savedScrollTop.value = e.target.scrollTop;
      savedScrollLeft.value = e.target.scrollLeft;
      updateMinimapPosition();
    }

    function onMinimapClick(e) {
      const mm = e.currentTarget;
      const rect = mm.getBoundingClientRect();
      // 点击位置相对于缩略图容器
      const clickY = e.clientY - rect.top;
      // viewport 高 30px，让点击点成为 viewport 中心
      let targetTop = clickY - 15;
      targetTop = Math.max(0, Math.min(rect.height - 30, targetTop));
      // 换算成 diff host 的滚动位置
      const ratio = rect.height > 30 ? targetTop / (rect.height - 30) : 0;
      const host = diffHostEl.value;
      if (!host) return;
      const max = host.scrollHeight - host.clientHeight;
      host.scrollTop = Math.max(0, Math.min(max, ratio * max));
    }

    const tooltipHtml = computed(() => {
      if (!lastEntry.value || !currentData.value) return '';
      const e = lastEntry.value, d = currentData.value;
      const sm = {added:'新增',modified:'修改',deleted:'删除',renamed:'重命名',unchanged:'未改动'};
      return `<div><b>路径:</b> ${esc(e.path)}</div><div><b>状态:</b> ${sm[e.status]||e.status}</div><div><b>大小:</b> ${d.size||0} B</div><div><b>修改时间:</b> ${d.modified?new Date(d.modified).toLocaleString():''}</div><div><b>语言:</b> ${esc(d.language||'')}</div>`;
    });

    const expandedDirsSet = computed(() => expandedDirs.value);

    const findCountText = computed(() => {
      if (!findQ.value) return '';
      return `${findIdx.value+1}/${findMatches.value.length}`;
    });

    // ---- diff rendering ----
    function highlightLine(line, lang) {
      if (!line) return ' ';
      if (window.hljs && lang) {
        try { return hljs.highlight(line, { language: lang, ignoreIllegals: true }).value; } catch(e){}
      }
      return esc(line);
    }

    function renderDiffHtml(oldText, newText) {
      const oldLines = oldText.replace(/\r\n/g,'\n').split('\n');
      const newLines = newText.replace(/\r\n/g,'\n').split('\n');
      while (oldLines.length && !oldLines[oldLines.length-1]) oldLines.pop();
      while (newLines.length && !newLines[newLines.length-1]) newLines.pop();
      const MAX = 5000;
      if (oldLines.length > MAX || newLines.length > MAX) {
        return { html: `<div class="placeholder">文件过大（${oldLines.length}/${newLines.length} 行），仅支持 ${MAX} 行以内的 diff</div>`, ops: [] };
      }
      const ext = lastEntry.value ? fileExt(lastEntry.value.path) : '';
      const lang = extToLang(ext);
      const n = oldLines.length, m = newLines.length;
      const dp = Array.from({length:n+1}, () => new Int32Array(m+1));
      for (let i = n-1; i >= 0; i--) for (let j = m-1; j >= 0; j--)
        dp[i][j] = oldLines[i] === newLines[j] ? dp[i+1][j+1]+1 : Math.max(dp[i+1][j], dp[i][j+1]);
      const ops = [];
      let i = 0, j = 0;
      while (i < n && j < m) {
        if (oldLines[i] === newLines[j]) { ops.push({t:'ctx',o:oldLines[i],n:newLines[j]}); i++; j++; }
        else if (dp[i+1][j] >= dp[i][j+1]) { ops.push({t:'del',o:oldLines[i]}); i++; }
        else { ops.push({t:'add',n:newLines[j]}); j++; }
      }
      while (i < n) { ops.push({t:'del',o:oldLines[i]}); i++; }
      while (j < m) { ops.push({t:'add',n:newLines[j]}); j++; }
      let html = '<table class="diff">';
      let oldLn = 1, newLn = 1;
      for (let idx = 0; idx < ops.length; idx++) {
        const op = ops[idx];
        if (op.t === 'ctx') { oldLn++; newLn++; }
        else if (op.t === 'del') { oldLn++; }
        else { newLn++; }
        if (op.t === 'ctx') html += `<tr class="ctx"><td class="ln">${oldLn-1}</td><td class="ln">${newLn-1}</td><td class="code">${highlightLine(op.o, lang)}</td></tr>`;
        else if (op.t === 'del') html += `<tr class="del"><td class="ln">${oldLn-1}</td><td class="ln"></td><td class="code">- ${highlightLine(op.o, lang)}</td></tr>`;
        else html += `<tr class="add"><td class="ln"></td><td class="ln">${newLn-1}</td><td class="code">+ ${highlightLine(op.n, lang)}</td></tr>`;
      }
      html += '</table>';
      return { html, ops };
    }

    // ---- actions ----
    async function loadChanges() {
      if (!activeRepo.value) return;
      try {
        const data = await send('listChanges');
        current.value = data.changeSet;
        // 自动展开顶层目录
        const topDirs = new Set();
        for (const e of data.changeSet?.entries || []) {
          const i = e.path.indexOf('/');
          if (i > 0) topDirs.add(e.path.slice(0, i));
        }
        if (topDirs.size && expandedDirs.value.size === 0) {
          expandedDirs.value = new Set([...topDirs]);
        }
      } catch(e) { errorBar.value = e.message; }
    }

    async function onRepoChange() {
      await send('selectRepo', { path: activeRepo.value });
      current.value = null;
      lastEntry.value = null;
      currentData.value = null;
      commitMode.value = false;
      currentCommitSha.value = '';
      currentCommitShort.value = '';
      commits.value = [];
      await loadChanges();
    }

    async function onAddRepo() {
      try {
        await send('addRepo');
        const data = await send('bootstrap');
        repos.value = data.repos || [];
        if (data.activeRepo) activeRepo.value = data.activeRepo;
        await loadChanges();
      } catch(e) {}
    }

    function toggleDir(item) {
      if (!item.children) return;
      if (expandedDirs.value.has(item.path)) expandedDirs.value.delete(item.path);
      else expandedDirs.value.add(item.path);
    }

    function dirIconClass(item) {
      if (!item.children) return '';
      if (item.hasModify) return 'dir-red';
      if (item.hasAdd) return 'dir-green';
      return '';
    }

    async function onNodeClick(item) {
      if (item.children) { toggleDir(item); return; }
      lastEntry.value = { path: item.path, insertions: item.insertions||0, deletions: item.deletions||0, status: item.status };
      rightTab.value = 'diff';
      // cache: add to openTabs if not already
      if (!openTabs.value.find(t => t.path === item.path)) {
        openTabs.value.push({ path: item.path, insertions: item.insertions||0, deletions: item.deletions||0, status: item.status });
      }
      try {
        currentData.value = await send('getFileContent', { path: item.path });
        editContent.value = currentData.value.newText || '';
        originalContent.value = editContent.value;
        await nextTick();
        updateMinimapPosition();
      } catch(e) { ElMessage.error(e.message); }
    }

    function switchTab(tab) {
      if (lastEntry.value && lastEntry.value.path === tab.path) return;
      lastEntry.value = { path: tab.path, insertions: tab.insertions, deletions: tab.deletions, status: tab.status };
      rightTab.value = 'diff';
      send('getFileContent', { path: tab.path }).then(d => {
        currentData.value = d;
        editContent.value = d.newText || '';
        originalContent.value = editContent.value;
        nextTick(updateMinimapPosition);
      }).catch(e => ElMessage.error(e.message));
    }

    function onTabChange() {
      nextTick(() => {
        if (rightTab.value === 'info' && editorEl.value) {
          editorEl.value.scrollTop = savedScrollTop.value;
          editorEl.value.scrollLeft = savedScrollLeft.value;
          const area = editorEl.value.closest('.editor-area');
          if (area) {
            const bg = area.querySelector('.highlight-bg');
            if (bg) bg.style.transform = `translate(${-savedScrollLeft.value}px, ${-savedScrollTop.value}px)`;
            const gutter = editorEl.value.closest('.code-editor').querySelector('.gutter-inner');
            if (gutter) gutter.style.transform = `translateY(${-savedScrollTop.value}px)`;
          }
        } else if (rightTab.value === 'diff' && diffHostEl.value) {
          diffHostEl.value.scrollTop = savedScrollTop.value;
          diffHostEl.value.scrollLeft = savedScrollLeft.value;
          nextTick(updateMinimapPosition);
        }
      });
    }

    const isDirty = computed(() => editContent.value !== originalContent.value);

    async function saveFile() {
      if (!lastEntry.value || !isDirty.value) return;
      try {
        await send('saveFile', { path: lastEntry.value.path, content: editContent.value });
        originalContent.value = editContent.value;
        ElMessage.success('已保存');
        loadChanges();
      } catch(e) { ElMessage.error(e.message); }
    }

    function cancelEdit() {
      editContent.value = originalContent.value;
    }

    function closeFile() {
      if (lastEntry.value) {
        openTabs.value = openTabs.value.filter(t => t.path !== lastEntry.value.path);
      }
      lastEntry.value = null;
      currentData.value = null;
      editContent.value = '';
      originalContent.value = '';
    }

    function closeTab(tab) {
      openTabs.value = openTabs.value.filter(t => t.path !== tab.path);
      if (lastEntry.value && lastEntry.value.path === tab.path) {
        lastEntry.value = null;
        currentData.value = null;
        editContent.value = '';
        originalContent.value = '';
      }
    }

    function navigateFile(dir) {
      const entries = visibleEntries.value.filter(e => e.status !== 'unchanged');
      if (!entries.length) return;
      let idx = entries.findIndex(e => e.path === lastEntry.value?.path);
      if (idx < 0) idx = dir > 0 ? -1 : 0;
      idx = (idx + dir + entries.length) % entries.length;
      onNodeClick(entries[idx]);
    }

    function toggleInline() { inlineDiff.value = !inlineDiff.value; }
    function toggleScope() {
      showAllFiles.value = !showAllFiles.value;
      if (showAllFiles.value && !allFiles.value.length) {
        send('listAllFiles').then(d => { allFiles.value = d.files || []; });
      }
    }

    function onContextMenu(e, item) {
      e.preventDefault();
      var opts = [];
      opts.push({ label: "打开所在文件夹", action: function(){ send("openInExplorer", { path: item.path }); } });
      opts.push({ label: ignoredPaths.value.has(item.path) ? "取消忽略" : "忽略", action: function(){ toggleIgnore(item.path); } });
      if (!item.children) {
        opts.push({ label: "删除", action: function(){
          ElMessageBox.confirm("确定要删除文件 \"" + item.path + "\" 吗？", "删除确认", { type: "warning", confirmButtonText: "删除", cancelButtonText: "取消" })
            .then(function(){ send("deleteFile", { path: item.path }).then(function(){ ElMessage.success("已删除"); loadChanges(); }); })
            .catch(function(){});
        }});
      }
      var menu = document.createElement("div");
      menu.className = "ctx-menu";
      opts.forEach(function(o){
        var it = document.createElement("div");
        it.className = "ctx-item";
        it.textContent = o.label;
        it.onclick = function(){ o.action(); menu.remove(); };
        menu.appendChild(it);
      });
      menu.style.left = e.clientX + "px";
      menu.style.top = e.clientY + "px";
      document.body.appendChild(menu);
      setTimeout(function(){ document.addEventListener("click", function(){ menu.remove(); }, { once: true }); }, 10);
    }

    function toggleIgnore(path) {
      if (ignoredPaths.value.has(path)) ignoredPaths.value.delete(path);
      else ignoredPaths.value.add(path);
      ignoredPaths.value = new Set(ignoredPaths.value);
    }

    async function toggleCommitPanel() {
      commitPanelOpen.value = !commitPanelOpen.value;
      if (commitPanelOpen.value && !commits.value.length) {
        try { const d = await send('listCommits'); commits.value = d.commits || []; } catch(e) {}
      }
    }

    async function onCommitClick(c) {
      currentCommitSha.value = c.sha;
      currentCommitShort.value = c.short;
      currentCommitMessage.value = c.message;
      currentCommitAuthor.value = c.author;
      currentCommitDate.value = formatDate(c.date);
      commitMode.value = true;
      lastEntry.value = null;
      currentData.value = null;
      try {
        const d = await send('getCommitDiff', { sha: c.sha });
        current.value = d.changeSet;
      } catch(e) { ElMessage.error(e.message); }
    }

    async function backToWorkingTree() {
      commitMode.value = false;
      currentCommitSha.value = '';
      currentCommitShort.value = '';
      currentCommitMessage.value = '';
      currentCommitAuthor.value = '';
      currentCommitDate.value = '';
      lastEntry.value = null;
      currentData.value = null;
      await loadChanges();
    }

    function formatDate(iso) {
      try {
        const d = new Date(iso);
        const p = n => String(n).padStart(2,'0');
        return `${d.getFullYear()}-${p(d.getMonth()+1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
      } catch(e) { return iso || ''; }
    }

    // ---- find/replace ----
    function doFind() { findMatches.value = []; findIdx.value = 0; }
    function findJump(dir) {}
    function closeFind() { findPanelVisible.value = false; findQ.value = ''; }
    function replaceOne() {}
    function replaceAll() {}

    // ---- keyboard ----
    onMounted(async () => {
      window.addEventListener('keydown', async (e) => {
        if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'r') { e.preventDefault(); loadChanges(); }
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'f') { e.preventDefault(); findPanelVisible.value = true; }
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); saveFile(); }
      });
      try {
        const data = await send('bootstrap');
        repos.value = data.repos || [];
        activeRepo.value = data.activeRepo || '';
        inlineDiff.value = data.settings?.inlineDiff !== false;
        if (activeRepo.value) await loadChanges();
      } catch(e) { errorBar.value = e.message; }
    });

    return {
      repos, activeRepo, current, allFiles, searchQ, filter, viewMode, showAllFiles,
      inlineDiff, rightTab, lastEntry, currentData, expandedDirs, editContent, isDirty,
      errorBar, findPanelVisible, findQ, replaceQ, replaceRowVisible, findMatches, findIdx,
      visibleEntries, treeData, statsText, diffHtml, minimapHtml, infoHtml, tooltipHtml, findCountText, expandedDirsSet, highlightedContent, lineNumbers, syncScroll, onDiffScroll,
      diffHostEl, editorEl, minimapEl, minimapViewportTop, onMinimapClick,
      autoCompleteVisible, autoCompleteItems, autoCompleteIndex, autoCompletePos, onEditorInput, onEditorKeydown, applySnippet,
      loadChanges, onRepoChange, onAddRepo, toggleDir, dirIconClass, onNodeClick, onTabChange,
      saveFile, cancelEdit, closeFile, navigateFile, toggleInline, toggleScope, onContextMenu,
      ignoredPaths, showIgnored, openTabs, switchTab, closeTab, toggleIgnore,
      doFind, findJump, closeFind, replaceOne, replaceAll,
      fileIcon, dirOf, baseOf,
      commitPanelOpen, commits, commitMode, currentCommitSha, currentCommitShort,
      currentCommitMessage, currentCommitAuthor, currentCommitDate, commitInfoOpen,
      toggleCommitPanel, onCommitClick, backToWorkingTree, formatDate,
    };
  }
}).use(ElementPlus).mount('#app');

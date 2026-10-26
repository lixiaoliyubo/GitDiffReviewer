# GitDiffReviewer

一个 Windows 桌面端 Git 代码审查工具。选一个 Git 仓库后，以文件树形式列出工作区未提交改动，点文件用行内 Diff 查看（带语法高亮、缩略图导航），详情页可直接编辑保存。支持选择历史 Commit 做对比基线。

## 技术栈

- **后端**：C# / .NET 8 WPF + WebView2
- **前端**：Vue 3 + Element Plus + highlight.js
- **风格**：深色 VS Code 主题

## 功能

### Diff 查看
- 工作区未提交改动 vs HEAD，行内红绿标记
- 语法高亮（Vue / TS / JS / CSS / HTML / JSON / C# / Python / Java / Go / Rust 等）
- 右侧缩略图（Minimap）：绿色=新增行，红色=删除行，点击跳转定位
- 行号、横向滚动、滚动同步

### 文件树
- 目录树结构展开/收起，红色文件夹=含修改，绿色=仅新增
- 平铺 / 树形 两种模式
- 全部文件 / 仅改动 切换
- 文件搜索、按状态筛选（全部/新增/修改/删除）
- 右键菜单：打开所在文件夹、忽略、删除（带确认）
- 忽略文件后可一键恢复显示

### 详情编辑
- 点击「详情」tab 直接查看和编辑文件内容
- 语法高亮 + 行号
- 有修改时自动显示「保存」「取消」按钮
- 代码片段自动补全（输入前缀弹出 VS Code 风格 IntelliSense）：
  - Vue：`<template>`、`<script setup>`、ref/computed/watch/defineProps 等
  - TS/JS：if/for/forEach/map/filter/promise/async/console/fetch 等
  - CSS：flex/grid/absolute/border-radius/box-shadow/transition 等
  - HTML：`<div>`/`<img>`/`<input>`/`<button>` 等

### 提交历史
- 顶部可折叠面板列出仓库所有 Commit
- 点某个 Commit 切换对比基线：工作区 vs 该 Commit
- 左侧底部显示当前选中提交的信息（作者、时间、消息）

### 其他
- 打开的文件缓存为 Tab 标签，切换不重新加载
- 多仓库支持，顶部下拉切换

## 快速开始

### 运行
1. 安装 [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 和 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win11 自带）
2. 克隆本仓库
3. 编译运行：

```powershell
dotnet publish src\GitDiffReviewer\GitDiffReviewer.csproj -c Release -r win-x64 --self-contained false -o publish
.\publish\GitDiffReviewer.exe
```

### 开发

```powershell
# 编译
dotnet build -c Release

# 跑测试
dotnet test -c Release

# 发布
dotnet publish src\GitDiffReviewer\GitDiffReviewer.csproj -c Release -r win-x64 --self-contained false -o publish
```

修改前端文件（`wwwroot/` 下的 html/js/css）后，复制到 `publish/wwwroot/` 并删除 `%APPDATA%\GitDiffReviewer\WebView2\` 缓存即可。

## 项目结构

```
├── src/GitDiffReviewer/
│   ├── App.xaml / MainWindow.xaml      # WPF 主窗口
│   ├── Bridge/MessageRouter.cs          # WebView2 消息路由
│   ├── Services/
│   │   ├── WorkingTreeService.cs        # 工作区 vs HEAD diff
│   │   ├── CommitService.cs             # 提交历史
│   │   ├── FileContentService.cs        # 文件内容读取
│   │   ├── GitCommands.cs / GitRunner.cs # git 命令执行
│   │   └── SettingsStore.cs             # 设置持久化
│   └── wwwroot/
│       ├── index.html                   # Vue 应用入口
│       ├── app.js                       # 全部前端逻辑
│       ├── style.css                    # 深色主题样式
│       └── highlight.min.js             # 语法高亮
└── tests/GitDiffReviewer.Tests/         # 单元测试
```

## 数据目录

所有数据存在 `%APPDATA%\GitDiffReviewer\`：
- `settings.json` — 仓库列表、设置
- `logs/` — 日志
- `WebView2/` — WebView2 缓存（可删）

## License

MIT
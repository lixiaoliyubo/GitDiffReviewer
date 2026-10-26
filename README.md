# GitDiffReviewer

独立 Windows 桌面工具：`git pull` 之后，列出"上次审阅基线 → 当前 HEAD"之间所有变更文件，用 Monaco 行内 diff 查看，一键标记已审阅。不写仓库、不联网、不需要任何 IDE。

## 使用

1. 解压 `GitDiffReviewer-win-x64.zip`，双击 `GitDiffReviewer.exe`。
   - 目标机器需安装 WebView2 Runtime（Win10 21H2 / Win11 自带）。
2. 首次启动点「选择仓库…」选一个 git 目录；工具自动把当前 HEAD 记为基线。
3. 在外部终端 `git pull`（或同事推了新提交后），回到本工具：左侧自动列出变更文件，点一行看红绿行内 diff。
4. 看完点「标记已审阅」，左侧清空；下次 pull 后再列新的变更。

### 三个场景

- **拉取后审阅**：早上 `git pull` 后打开工具，自动列出本次拉进来的全部改动。
- **跨多个 commit**：同事一周推了 N 个提交，一次看到合并后的全部改动，按文件列。
- **多仓库**：顶部下拉切换，每个仓库各自记住自己的基线。

### 快捷键

| 键 | 作用 |
|---|---|
| F7 / Shift+F7 | 下一处 / 上一处改动（diff 聚焦时） |
| Ctrl+R | 刷新 |
| Ctrl+Shift+M | 标记已审阅 |
| Ctrl+Shift+I | 切换行内 / 并排视图 |

## 数据与安全

- 所有状态写在 `%APPDATA%\GitDiffReviewer\`：`settings.json`、`state\<repoKey>.json`、`logs\`、`WebView2\`。
- 对仓库只跑 7 条只读 git 命令（rev-parse / merge-base / diff / show / log），不执行 pull/commit/checkout/reset 等任何写操作。
- 不启 HTTP 服务，不监听端口；Web UI 通过 WebView2 虚拟主机 `https://app.local/` 加载，Monaco 全部本地文件，运行时零网络请求。

## 已知限制

- 未做：未提交改动（`git status`）展示、代码评审批注、导出报告、远程仓库访问、托盘常驻、安装器。
- 大文件单侧超过 20000 行时只显示前 20000 行并提示截断。
- 变更列表超过 3000 条会截断计数。
- 二进制文件显示占位；png/jpg/gif/webp/svg 显示新旧对比图。

## 决策记录

- 形态选 WPF + WebView2（非 VS Code 扩展、非 Web 服务）：双击即用、不装 Node、无端口。
- git 调用统一走 `GitRunner`，只许 `ArgumentList`，stdout 拿原始字节再按字段解码（严格 UTF-8 → GB18030 回退）。
- 基线不自动推进：HEAD 变化只更新 `lastSeenHead`，只有「标记已审阅」或「手选提交」才动 `baselineHead`，避免"拉了没看"的改动丢失。
- Monaco 从 npm tarball 解出 `min/vs` 离线放 `wwwroot/vendor/monaco/vs/`，AMD loader 加载，不引 CDN。
- 状态文件用临时文件 + `File.Move(overwrite:true)` 原子替换，避免半截 JSON。

## 开发

```powershell
dotnet build -c Release      # 编译
dotnet test -c Release      # 跑单测
.\scripts\make-fixture.ps1  # 在 %TEMP%\gdr-fixture 造 7 条变更的 fixture
.\scripts\fetch-monaco.ps1  # 重新拉 Monaco 离线包
dotnet publish src\GitDiffReviewer\GitDiffReviewer.csproj -c Release -r win-x64 --self-contained false -o publish
```

## 验收结果

| # | 项 | 状态 | 说明 |
|---|---|---|---|
| AC-1 | 独立形态，干净机器解压双击启动 | ✅ 代码就绪 / 待真机 | 开发机已验证启动；干净机器需有 WebView2 Runtime |
| AC-2 | 首次无配置出现"选择仓库"，添加后记住 | ✅ 已实现 | bootstrap 空态 + OpenFolderDialog + settings.repos |
| AC-3 | fixture 7 条，状态/行数与原生命令一致 | ✅ 单测通过 | `ChangeSetServiceFixtureTests` |
| AC-4 | 基线不自动推进，重启后待审阅数不变 | ✅ 单测通过 | `BaselineServiceTests.重启后_基线不自动推进` |
| AC-5 | 标记已审阅后列表清空、重启仍为空 | ✅ 已实现 | `MarkReviewedAsync` 写 state 文件 |
| AC-6 | diff 行内默认，可切并排 | ✅ 已实现 | `renderSideBySide:false`，按钮 `updateOptions` |
| AC-7 | F7 / Shift+F7 跳转改动 | ✅ 已实现 | standalone 版注册 `diffReview.next/prev` |
| AC-8 | GBK 文件中文不乱码 | ✅ 单测通过 | `EncodingUtilTests` 严格 UTF-8 失败回退 GB18030 |
| AC-9 | 二进制占位；图片新旧预览 | ✅ 已实现 | FileContentService + base64 img |
| AC-10 | 大文件截断提示 | ✅ 已实现 | 单侧 >20000 行截断 + 黄条 |
| AC-11 | 多仓库各自基线互不干扰 | ✅ 已实现 | repoKey = SHA256(路径) 前 16 hex |
| AC-12 | 外部 pull 后 2 秒内自动刷新 | ✅ 已实现 | FileSystemWatcher 800ms 防抖 + 30s 轮询兜底 |
| AC-13 | 只读性：不写仓库 | ✅ 已实现 | 仅 7 条只读白名单命令 |
| AC-14 | 无端口监听 | ✅ 已实现 | 虚拟主机映射，无 HttpListener |
| AC-15 | 断网可用 | ✅ 已实现 | Monaco 本地，无 CDN |
| AC-16 | 路径越界被拒 | ✅ 单测通过 | `PathGuardTests` + 变更集查表 |
| AC-17 | `dotnet build -c Release` 0 错 0 警，单测全绿 | ✅ 通过 | 31/31 测试通过 |
| AC-18 | 界面/错误/日志全中文 | ✅ 已实现 | 文案、日志、异常均中文 |

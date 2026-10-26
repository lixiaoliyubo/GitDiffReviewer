# 下载 monaco-editor npm tarball，只保留 package/min/vs 到 wwwroot/vendor/monaco/vs（§12.2）。
$ErrorActionPreference = 'Stop'
$version = '0.57.0'
$url = "https://registry.npmjs.org/monaco-editor/-/monaco-editor-$version.tgz"
$tmp = Join-Path $env:TEMP "monaco-$version.tgz"
$extractDir = Join-Path $env:TEMP "monaco-$version"
$dest = 'E:\GitDiffReviewer\src\GitDiffReviewer\wwwroot\vendor\monaco\vs'

Write-Host "下载 $url"
Invoke-WebRequest -Uri $url -OutFile $tmp -TimeoutSec 120

if (Test-Path $extractDir) { Remove-Item -Recurse -Force $extractDir }
New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
# tar (bsdtar) 解 tgz
tar -xzf $tmp -C $extractDir
if ($LASTEXITCODE -ne 0) { throw "tar 解压失败" }

if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
# package/min/vs -> wwwroot/vendor/monaco/vs
Copy-Item -Recurse (Join-Path $extractDir 'package\min\vs') $dest

Write-Host "完成：$dest"
Get-ChildItem $dest | Select-Object Name | Format-Table -AutoSize

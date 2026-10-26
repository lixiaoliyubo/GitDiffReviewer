# 在 %TEMP%\gdr-fixture 造一个仓库，BASE..HEAD 恰好 7 条变更（§15）。
$ErrorActionPreference = 'Stop'

$repo = Join-Path $env:TEMP 'gdr-fixture'
if (Test-Path $repo) { Remove-Item -Recurse -Force $repo }
New-Item -ItemType Directory -Force -Path $repo | Out-Null

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$GitArgs)
    $errFile = [IO.Path]::GetTempFileName()
    try {
        & git -C $repo @GitArgs 2>$errFile | Out-Null
        $code = $LASTEXITCODE
    } finally {
        Remove-Item $errFile -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) { throw "git $GitArgs failed ($code)" }
}

Invoke-Git init -q
Invoke-Git config user.email 'test@test.com'
Invoke-Git config user.name 'test'

# --- BASE 提交：modify.txt(v1) / delete.txt / rename-old.txt ---
Set-Content -LiteralPath (Join-Path $repo 'modify.txt') -Value "line1`nline2`n" -NoNewline -Encoding utf8
Set-Content -LiteralPath (Join-Path $repo 'delete.txt') -Value 'bye' -Encoding utf8
Set-Content -LiteralPath (Join-Path $repo 'rename-old.txt') -Value 'same' -Encoding utf8
Invoke-Git add -A
Invoke-Git commit -q -m base

$base = (& git -C $repo rev-parse HEAD).Trim()

# --- HEAD 提交：7 条变更 ---
Set-Content -LiteralPath (Join-Path $repo 'modify.txt') -Value "line1`nline2`nline3`nline4`n" -NoNewline -Encoding utf8  # 1 M
Set-Content -LiteralPath (Join-Path $repo 'added.txt') -Value "a`nb" -NoNewline -Encoding utf8                       # 2 A
New-Item -ItemType Directory -Force -Path (Join-Path $repo 'sub') | Out-Null
Set-Content -LiteralPath (Join-Path $repo 'sub\中文新增.txt') -Value 'x' -Encoding utf8                                # 3 A 中文路径
Remove-Item -LiteralPath (Join-Path $repo 'delete.txt')                                                               # 4 D
Invoke-Git mv (Join-Path $repo 'rename-old.txt') (Join-Path $repo 'rename-new.txt')                                   # 5 R100
[IO.File]::WriteAllBytes((Join-Path $repo 'bin.dat'), [byte[]](1,2,0,3,0,4))                                          # 6 二进制
# "中文内容" 的 GBK 字节：中=D6D0 文=CEC4 内=C4DA 容=C8DD
[IO.File]::WriteAllBytes((Join-Path $repo 'gbk.txt'), [byte[]](0xD6,0xD0,0xCE,0xC4,0xC4,0xDA,0xC8,0xDD))  # 7 GBK

Invoke-Git add -A
Invoke-Git commit -q -m head

$head = (& git -C $repo rev-parse HEAD).Trim()

Write-Host "REPO=$repo"
Write-Host "BASE=$base"
Write-Host "HEAD=$head"

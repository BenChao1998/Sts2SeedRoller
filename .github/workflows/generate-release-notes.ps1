# 生成 release notes
# 参数: $args[0] = tag名称 (如 v1.2.3), $args[1] = 版本后缀 (如 -1.2.3)

$tagName = "$($args[0])"
$vs = "$($args[1])"

$headerLines = @(
    "## 下载说明",
    "",
    "### UI 版本 (sts2seedroller-win-x64$vs.zip)",
    "带快捷方式，开箱即用。解压后双击 `Sts2SeedRoller.lnk` 启动。",
    "",
    "### CLI 版本 (sts2seedroller-cli-win-x64$vs.zip)",
    "命令行工具，解压后将目录加入 PATH 即可使用。运行 `Sts2SeedRoller.exe --help` 查看帮助。",
    "",
    "### 单文件版 (Sts2SeedRollerUi$vs.exe / Sts2SeedRoller$vs.exe)",
    "绿色单文件，无需安装。直接双击运行。"
) -join "`n"

$previousTag = git describe --tags --abbrev=0 "$tagName^" 2>$null
if ($previousTag) {
    $changelog = git log "$previousTag..$tagName" --pretty=format:"- %s" --no-merges
    $headerLines + "`n`n## 变更日志`n`n" + $changelog | Out-File -FilePath release-notes.md -Encoding utf8
} else {
    $headerLines | Out-File -FilePath release-notes.md -Encoding utf8
}
"releaseNotesPath=release-notes.md" | Out-File -FilePath $env:GITHUB_OUTPUT -Append

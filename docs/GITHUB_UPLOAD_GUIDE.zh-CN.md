# GitHub 上传教程（项目所有者）

这份教程用于把已经生成的 **GitHub 源码包**发布成仓库，不用于上传 Windows 安装包。

## 1. 上传前的三个决定

在仓库设为 Public 之前，项目所有者必须确认：

1. 核对 `NOTICE` 已写明 `Copyright 2026 Xiang Zhang`，代码采用 Apache-2.0。
2. 核对“小夜”角色和 UI 插画继续使用 [ASSET_LICENSE.md](../ASSET_LICENSE.md) 的个人非商业授权；商业发行必须替换素材。
3. 首次只发布源码，不上传旧的 Windows ZIP、模型权重、真实测试音频和内部评测结果。

详见 [GITHUB_READINESS.zh-CN.md](GITHUB_READINESS.zh-CN.md)。

## 2. 使用准备好的源码包

本项目脚本会生成：

```text
publish\github-source\voice-memory-demo\
publish\github-source\VoiceMemo-X-版本号-github-source.zip
publish\github-source\VoiceMemo-X-版本号-github-source.zip.sha256
publish\github-source\VoiceMemo-X-版本号-github-source.manifest.json
```

应把解压后的 `voice-memory-demo` 文件夹作为仓库根目录。不要把整个 ZIP 当作一个文件上传到仓库。

发布前可再次验证：

```powershell
.\scripts\Test-GitHubSourcePackage.ps1 -Build
```

## 3. 推荐：GitHub Desktop

当前源码包超过 GitHub 网页一次可上传的 100 个文件限制，因此不推荐浏览器拖拽。

1. 安装并登录 GitHub Desktop。
2. 解压 GitHub 源码包。
3. 在 GitHub Desktop 选择 **File → Add Local Repository**，选择解压后的 `voice-memory-demo`。
4. 如果提示该目录还不是 Git 仓库，选择在此创建仓库。
5. 检查 Changes 清单：不应出现 `publish`、`test-data`、`test-results`、`artifacts`、`tools`、模型权重或真实密钥。
6. 首次提交信息可写 `Initial public source release`。
7. 点击 **Publish repository**。
8. 发布前保留最终角色素材的人工复核记录，并确保 [ASSET_LICENSE.md](../ASSET_LICENSE.md) 与仓库许可证说明同时存在。

官方说明：[用 GitHub Desktop 添加现有项目](https://docs.github.com/en/desktop/adding-and-cloning-repositories/adding-an-existing-project-to-github-using-github-desktop)。

## 4. 命令行方法

目标仓库地址为 <https://github.com/shawnzhang-lab/VoiceMemo-X>。如果需要重新发布到新仓库，先创建空仓库，不要自动生成 README、LICENSE 或 `.gitignore`，然后在解压后的源码包目录执行：

```powershell
git init -b main
. .\scripts\PublicReleaseFiles.ps1
$root = (Get-Location).Path
$inventory = Get-PublicReleaseInventory -ProjectRoot $root
foreach ($file in $inventory.Files) {
    $relative = $file.FullName.Substring($root.Length).TrimStart('\')
    git add -- $relative
}
git status --short
git commit -m "Initial public source release"
git remote add origin https://github.com/shawnzhang-lab/VoiceMemo-X.git
git push -u origin main
```

不要使用 `git add .` 或 `git add -A`。`git status --short` 必须人工检查后再 commit。若出现不属于源码包的文件，立即停止，不要用强制命令绕过。

官方说明：[把本地代码添加到 GitHub](https://docs.github.com/en/repositories/creating-and-managing-repositories/adding-locally-hosted-code-to-github)。

## 5. 上传后的检查

1. 等待 GitHub Actions 的 Windows CI 全部通过。
2. 在另一台机器或全新目录 clone，运行：

   ```powershell
   .\scripts\Test-PublicRelease.ps1
   ```

3. 检查 README 的所有本地链接和图片。
4. 开启 Dependabot、Private vulnerability reporting 和 main 分支保护。
5. Release 只附带从公开源码包干净构建的无模型 Lite ZIP 及 `.sha256`；不附带未经许可复核的模型或旧 Windows 包。

本教程只准备上传流程，不代表脚本会替你执行 `git init`、commit、push、创建远程仓库或发布 Release。

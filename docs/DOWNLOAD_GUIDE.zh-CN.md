# 下载教程

这份教程面向不熟悉 GitHub 的普通用户。下载前先确认你需要的是“可直接运行的 Windows 版本”还是“源码”。

## 1. 普通用户：下载 Windows 版本

只有项目 GitHub 页面右侧出现 **Releases**，且 Release 附件中存在类似以下文件时，才代表发布了可运行版本：

```text
VoiceMemo-X-0.9.10-win-x64-lite.zip
VoiceMemo-X-0.9.10-win-x64-lite.zip.sha256
```

步骤：

1. 打开项目 GitHub 页面。
2. 点击右侧 **Releases**，进入最新的正式版本；不要从 Issue、评论区或第三方网盘下载。
3. 在 **Assets** 中同时下载 Windows ZIP 和同名 `.sha256` 文件。
4. 打开 PowerShell，运行：

   ```powershell
   Get-FileHash "$HOME\Downloads\VoiceMemo-X-0.9.10-win-x64-lite.zip" -Algorithm SHA256
   ```

5. 把结果与 `.sha256` 文件中的值比较；完全一致后再解压。

> 当前 Release 提供的是不含 ONNX 模型权重的 **Lite 包**。它仍需要用户自备腾讯云和 DeepSeek API；本地语义向量检索和本地说话人数预判会降级。Windows 程序尚未代码签名，只有在文件来自官方 Release 且 SHA-256 一致时才继续运行。

## 2. 开发者：下载源码

### 方法 A：使用 Git

```powershell
git clone https://github.com/shawnzhang-lab/VoiceMemo-X.git
cd VoiceMemo-X
```

### 方法 B：网页下载源码 ZIP

1. 打开仓库首页。
2. 点击绿色 **Code** 按钮。
3. 点击 **Download ZIP**。
4. 解压后进入 `VoiceMemo-X` 文件夹。

网页下载的是某个提交的源码快照，不包含 Git 历史，也不包含被项目排除的 ONNX 模型权重。

## 3. 下载后该看哪份教程

- 普通用户安装：[WINDOWS_INSTALL_GUIDE.zh-CN.md](WINDOWS_INSTALL_GUIDE.zh-CN.md)
- 从源码启动：[INSTALLATION.zh-CN.md](INSTALLATION.zh-CN.md)
- 首次使用：[FIRST_USE_GUIDE.zh-CN.md](FIRST_USE_GUIDE.zh-CN.md)
- API 配置入口：[API_SETUP_INDEX.zh-CN.md](API_SETUP_INDEX.zh-CN.md)

GitHub 官方下载说明：

- [下载仓库中的文件](https://docs.github.com/en/repositories/working-with-files/using-files/downloading-files-from-github)
- [下载源码归档](https://docs.github.com/en/repositories/working-with-files/using-files/downloading-source-code-archives)
- [了解 GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)

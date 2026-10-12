# 首次发布到 GitHub

## 准备好的文件

- **源码文件夹**：`每日一记吧-GitHub`，含 README、MIT 许可、源码、图标、构建脚本和 GitHub Actions。
- **发布包**：`dist/DailyReflection-v1.1.1-windows-portable.zip`，供普通用户下载运行。
- **校验文件**：上述 ZIP 旁边的同名 `.zip.sha256`。

源码包里的 `bin`、`dist` 和 `artifacts` 是本机构建产物，已被 `.gitignore` 排除。上传源码时，请使用专门整理的源码上传 ZIP，解压后上传其中的文件。

## 1. 新建仓库

1. 登录 [GitHub](https://github.com)，点击右上角 **+ → New repository**。
2. 仓库名建议用 `daily-reflection`，描述可填「睡前用两三句话记下今天，保存后安心关机。」
3. 选择 **Public**，方便其他人查看和下载。
4. 不勾选自动生成 README、`.gitignore` 和 License，整理的源码已经包含这些文件。
5. 点击 **Create repository**。

## 2. 上传源码

无需命令行的办法：

1. 解压 **每日一记吧-GitHub源码上传包-v1.1.1.zip**。
2. 在空仓库页面点击 **uploading an existing file**；已有文件的仓库点击 **Add file → Upload files**。
3. 打开解压后的源码文件夹，选中里面的文件和文件夹，拖到上传页面。README 应位于仓库根目录，不要把外层 `每日一记吧-GitHub` 文件夹作为额外层级上传，也不要只上传 ZIP 本身。
4. 检查包含 `.github`、`.gitignore`、`.gitattributes`、`README.md`、`LICENSE`、`src` 和 `docs`。在文件资源管理器中开启「查看 → 显示 → 隐藏的项目」后检查点号开头的文件。
5. 提交说明填 `Initial release of DailyReflection v1.1.1`，提交到主分支。
6. 仓库首页应显示介绍和截图；若已启用 Actions，可在 **Actions** 页面查看构建结果。

也可以通过 Git 上传完整源码文件夹。以下命令需要把仓库地址改成刚创建的真实地址；每次只执行一行：

```powershell
git init
git branch -M main
git add .
git status --short
git commit -m "Initial release of DailyReflection v1.1.1"
git remote add origin https://github.com/你的用户名/daily-reflection.git
git push -u origin main
```

在 `git add .` 后检查 `git status --short`，确认没有 `数据`、`entries.json`、快捷方式和构建输出。若 Git 提示缺少身份，按提示设置自己的提交姓名和邮箱。首次推送按 Git 的登录提示授权。

## 3. 发布可下载的软件

1. 打开仓库右侧 **Releases → Draft a new release**。
2. 创建标签 **v1.1.1**，Target 选择上传源码的主分支。
3. 标题填 **每日一记吧 v1.1.1**。
4. 将 `docs/RELEASE-v1.1.1.md` 的正文复制到描述框。
5. 将 **DailyReflection-v1.1.1-windows-portable.zip** 和同名 **.zip.sha256** 拖到附件区域。
6. 附件上传完成后点击 **Publish release**。

源码和软件分别上传：源码进入仓库文件列表，运行包进入 Release 附件。普通用户下载带 **windows-portable** 的 ZIP；GitHub 自动生成的 **Source code (zip)** 是给开发者的源码。

## 以后更新

1. 修改源码和 `src/AssemblyInfo.cs` 中的两个版本号，更新 README 中的下载文件名和发布说明。
2. 在 Windows PowerShell 中运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1`。
3. 检查 `artifacts/test-results.txt` 与 `dist` 中的新发布包，再提交源码。
4. 创建对应的新版本 Release，上传 ZIP 和 SHA-256 文件。

GitHub Actions 也会生成经过验证的 ZIP，完成构建后可从该次运行的 Artifacts 下载。对外发布仍使用 Releases 的持久下载入口。

## 官方操作说明

- [新建仓库](https://docs.github.com/en/repositories/creating-and-managing-repositories/creating-a-new-repository)
- [上传文件到仓库](https://docs.github.com/en/repositories/working-with-files/managing-files/adding-a-file-to-a-repository)
- [创建和管理 Release](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)

# API Key 管理器

本地加密的 Windows 密钥管理工具。新版采用「温润档案风」：暖灰纸白与低饱和绿色，支持深色主题，列表和详情分开显示。数据不联网、不上传。

## 下载正式版

[GitHub Releases：v2.0.0](https://github.com/v1Ta44/api-key-manager/releases/tag/v2.0.0)。推荐下载 `api-key-manager-2.0.0-win-x64-sc.zip`，完整解压后运行 `ApiKeyManager.exe`，无需另装 .NET。`fd.zip` 适合已安装 x64 .NET 9 Desktop Runtime 9.0.20 或更新 9.0 补丁版的环境。发行附件提供 SHA-256 校验值和升级说明。

维护者已明确跳过剩余人工环境检查并授权发布；这些项目保留为未验证，详见 [发行说明](docs/release-notes-2.0.md)。程序未作开发者代码签名。

## 试用新版

需要 Windows 10/11 x64 和 .NET 9 SDK 9.0.318（或同功能带更新补丁）才能从源码构建，版本由 `global.json` 固定。运行：

```powershell
./tools/preview-frontend.ps1
```

脚本启动正式新版的隔离演示，自动生成合成记录、自动解锁，不读取真实库。演示主密码为 `demo`。直接双击正式程序则使用正常数据目录。

首次使用先设置至少 8 位的主密码。主密码无法找回。主界面支持搜索、平台/标签/到期筛选、复制、单条密钥显示 10 秒；宽窗口显示详情栏，窄窗口返回列表。编辑使用独立草稿，保存失败保留输入供重试。

「数据与设置」中可导出加密备份、预览后合并或替换导入、修改主密码、设置自动锁定与剪贴板清理。CSV 包含明文密钥，需要单独确认。替换导入前必须成功保存当前库的恢复副本。

快捷键：`Ctrl+F` 查找、`Ctrl+N` 新增、`Ctrl+L` 锁定、`Ctrl+T` 切换主题；列表聚焦时 `Ctrl+C` 复制密钥、`Enter` 查看详情；`Escape` 取消面板。

## 数据兼容与保护

- 保留原 v1 `vault.akv` / `.akvbak` 格式、AES-256-GCM、PBKDF2 参数策略、全部记录字段与 CSV 列。`ExpiresUtc` 保留原有本地日期语义。
- 新保存先写候选文件、解密回读校验，再替换原文件；成功后才更新界面。改密码同样先验证写入。
- 同一库用独占锁文件句柄避免新版多实例写入冲突；不同目录可以分别运行。不保证与不遵守锁协议的旧版本同时写入安全。
- 自动锁定会清除未保存草稿和可见明文，提前 30 秒提示。已经开始的磁盘操作可能完成，但不会把记录重新显示到锁定界面。
- 剪贴板启用自动清理后由独立计时器驱动，只清理内容仍匹配本次复制的记录。占用时最多尝试五次；退出后的失败无法继续重试。
- `settings.json` 保留主题和有效时间设置，旧 `ShowKeys=true` 不再恢复全局明文显示。设置保存失败会提示。
- 清空界面和释放引用不代表托管内存中的所有密钥字节已被物理擦除。

正常数据优先保存在程序所在目录，不可写时回退 `%APPDATA%\ApiKeyManager`。文件为 `vault.akv`、`settings.json`、`vault.akv.lock`；恢复副本为 `vault.akv.<时间>.bak`。锁文件残留不代表正在占用，以操作系统句柄为准。

## 工程结构

| 部分 | 职责 |
| --- | --- |
| `ApiKeyManager.Core` | 加密格式、数据字段、日期、CSV、剪贴板清理规则，无界面依赖 |
| `ApiKeyManager.Application` | 会话、保存事务、导入预览/提交、备份、密码变更，无 WPF 依赖 |
| `Wpf/ViewModels/WorkspaceViewModel` | 搜索/选择/面板状态、用例编排，列表使用不含密钥的摘要 |
| `Wpf/Workspace` | 生产窗口、焦点/响应布局、隔离检查器 |
| `Wpf/Services` | Windows 剪贴板和闲置活动适配 |
| `Wpf/Themes` | 深浅配色和共享控件样式 |
| `Wpf/Prototype` | 历史方向对照原型，只使用内存示例 |

## 验证与本地打包

```powershell
dotnet test ./ApiKeyManager.sln
./tools/verify-workspace.ps1
./tools/verify-publish.ps1
./tools/verify-release.ps1
```

窗口检查创建屏幕外的实际 WPF 窗口，运行控件事件与隔离加密库，输出截图和 `checks.json`；剪贴板使用注入适配器，不操作用户剪贴板。旧 `ui-*.ps1` 入口已转到该检查器，不再声称执行系统键鼠验收。

本地打包每次创建 `artifacts/publish/<时间>/`，不删除旧目录、不结束用户进程、不对外发布。框架依赖单文件版需要 x64 .NET 9 Desktop Runtime 9.0.20 或更新的 9.0 补丁版；自包含版携带运行时和 WPF 原生组件。体积以该次 `publish-results.json` 为准。

`verify-release.ps1` 顺序执行锁定依赖恢复、Release 测试、警告视为错误的构建、已知漏洞/弃用依赖查询、两种打包、压缩包完整性及隔离启动检查。输出位于 `artifacts/release-check-<时间>/`，包括候选 ZIP、SHA-256、版本和依赖许可说明。自动检查通过仍保留人工环境验收状态。

本机使用隔离工具链时可运行 `./tools/verify-release.ps1 -DotnetPath ./artifacts/toolchain/dotnet-9.0.318/dotnet.exe`。这是项目内工具目录，不需要更改系统的 .NET 安装。发行使用和升级说明见 [2.0 候选说明](docs/release-notes-2.0.md)。

详见 [重构实施与验证记录](docs/frontend-refactor-results.md)。用户已确认核心键鼠路径、系统剪贴板清理、真实闲置锁定和新顶栏基本操作；100/125/150/200% 系统缩放、多屏、完整键盘路径和读屏仍需实机补验，离屏渲染不替代这些结果。

## 命令行

| 参数 | 用途 |
| --- | --- |
| `--demo` | 新临时目录中的合成数据；自动解锁，密码 `demo` |
| `--dir <目录>` | 指定数据目录；与 `--demo` 一起使用时要求没有现有库及设置，拒绝覆盖 |
| `--dark` | 本次启动优先使用深色 |
| `--workspace-check <输出目录>` | 新版生产窗口、隔离文件流程、布局与性能检查 |
| `--layoutcheck` / `--dialogcheck` | 兼容旧参数，改跑新版检查，输出到随机临时目录 |
| `--prototype` | 历史 A/B 方向对照原型，不持久保存 |
| `--prototype-check <输出目录>` | 历史原型布局检查 |
| `--selftest` | 加密与基础功能冒烟检查 |
| `--makeicon <文件>` / `--glyphcheck` | 构建辅助工具 |

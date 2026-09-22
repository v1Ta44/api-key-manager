# API Key 管理器（ApiKeyManager）

一个单文件、免安装的 Windows 小工具，用于本地集中管理各家平台的 API Key。
所有数据用主密码加密后存放在本地 `vault.akv`，不联网、不上传。

## 一、成品

| 文件 | 说明 |
| --- | --- |
| `dist\standalone\ApiKeyManager.exe` | **推荐**。自包含单文件（约 50 MB），双击即用，目标机器无需安装任何运行时 |
| `dist\net-runtime\ApiKeyManager.exe` | 精简单文件（约 230 KB），需目标机器已装 .NET 9 Desktop Runtime |

均为 x64 Windows 桌面程序（WinForms，Windows 10/11）。

**界面效果**：`shots\light.png`（浅色）、`shots\dark.png`（深色）、`shots\dialog.png`（编辑对话框）

## 二、界面

- 现代卡片式布局：圆角卡片、圆角按钮、Segoe MDL2 图标、程序图标（钥匙）
- **浅色 / 深色双主题**：右上角「深色 / 浅色」一键切换，即时生效并记忆
- 列表：斑马纹、细分隔线、行高 38、密钥列等宽字体（Consolas）
- 输入框聚焦高亮描边、按钮悬停 / 按下反馈、状态栏倒计时胶囊
- 快捷键：`Enter` 编辑、`Delete` 删除、`Ctrl+C` 复制 Key、双击编辑；列头点击排序

## 三、功能

- **主密码保护**：首次启动设置主密码（≥8 位），全部数据 AES-256-GCM 加密存储
- **记录字段**：名称、提供商/平台、API Key、Base URL、默认模型、标签、备注、**到期日**、创建/更新时间
- **搜索过滤**：按名称 / 提供商 / 标签 / 备注 / URL / 模型即时模糊过滤
- **密钥掩码**：列表默认显示 `sk-1••••••••••••abcd`，可一键「显示密钥 / 隐藏密钥」
- **一键复制**：复制 Key / 复制 Base URL；复制的内容 **30 秒后自动清除剪贴板**（可配）
- **到期提醒**：可为每个 Key 设置到期日；列表中「到期日」列对已过期标红、14 天内到期标橙，
  解锁时弹一次汇总提醒；勾「不设置到期提醒」即退出该机制
- **自动锁定**：闲置 N 分钟（默认 5 分钟）自动回到锁定界面，可手动锁定、可关闭
- **加密强度自动升级**：解锁成功后若发现库文件用的是偏低的 PBKDF2 迭代数，
  会先备份再用当前推荐值（600 000 次）原地重加密，并回读校验；失败不影响本次使用
- **备份与迁移**：
  - 导出加密备份（`.akvbak`，可单独设置备份密码，**并一并保存界面偏好**）；导入时支持「替换」或「合并」
  - 「替换」是破坏性操作，覆盖前会**自动把当前库复制为 `vault.akv.<时间戳>.bak`**
  - 导出明文 CSV（带警告确认，便于迁移；用完请删除）
- **修改主密码**：一键全库重加密
- **便携模式**：数据默认与 exe 同目录（拷走整个文件夹即带走数据）；若目录不可写，自动落到 `%APPDATA%\ApiKeyManager\`

## 四、使用说明

1. 双击 `ApiKeyManager.exe` → 设置主密码（**忘记主密码无法恢复数据**，请牢记）
2. 点击「新增」录入 API Key；在列表中选中后可 编辑 / 复制 / 删除
3. 换机或备份：用「导出备份」生成 `.akvbak`，在新机器「导入备份」并输入备份密码
4. 离开工位可点「锁定」，或等自动锁定生效；右上角可切换深色主题

**数据文件**
- `vault.akv`：加密库（真正的数据，密文）
- `settings.json`：界面偏好（主题、自动锁定分钟数、剪贴板清除秒数、是否显示密钥），不含秘密

## 五、安全设计

| 项目 | 实现 |
| --- | --- |
| 加密算法 | AES-256-GCM（认证加密，篡改即解密失败） |
| 口令派生 | PBKDF2-HMAC-SHA256，默认 **600 000** 次迭代，16 字节随机盐 |
| 强度升级 | 迭代数存在明文文件头里（派生口令必须先知道它）。解锁成功后若发现低于阈值（300 000），自动备份并以 600 000 次原地重加密 |
| 完整性绑定 | 文件头（版本/迭代数/盐/nonce）作为 AAD 参与认证 |
| 写入方式 | 临时文件 + 原子替换，避免写一半损坏库 |
| 剪贴板 | 复制的密钥 30 秒后自动清除（内容仍是原文才清除，避免误删他人的复制） |
| 会话 | 闲置自动锁定、退出时清剪贴板；主密码仅驻留内存 |

> **已知取舍**：迭代数位于未认证的明文头中，因此文件名 + 头信息可以暴露"这个库用了多少轮派生"。
> 这是该文件格式的必要代价（否则无法派生出密钥去解密），不影响机密性与完整性。

> 提醒：导出的 CSV 是明文；屏幕上的「显示密钥」会明文展示，请留意周围环境。

## 六、命令行参数

| 参数 | 作用 |
| --- | --- |
| `--selftest` | 无界面自检（9 项，全部 PASS 时退出码 0）。发布前冒烟用，目标机器无需 SDK |
| `--demo` | 演示模式：临时目录生成 6 条示例数据并自动解锁（不碰真实数据） |
| `--dark` | 以深色主题启动 |
| `--preview` | 配合 `--demo`：自动打开示例编辑对话框（界面预览） |
| `--makeicon <路径>` | 生成程序图标 `icon.ico`（构建用） |
| `--glyphcheck` | 校验按钮图标字形在 Segoe MDL2 中的覆盖（构建用） |

> `--demo` 会把数据目录整体重定向到 `%TEMP%\akm-demo`，并且**该重定向在整个进程存活期间有效**——
> `--demo --dark` 之类的组合只会改演示目录里的 `settings.json`，不会污染你真实的偏好设置。

## 七、测试与开发

```powershell
# 单元测试（需要 .NET 9 SDK）；134 项，覆盖加密往返 / 防篡改 / 迭代升级 / 到期判定 /
# CSV 注入 / 剪贴板清理 / 目录解析 / 无障碍元数据
dotnet test .\ApiKeyManager.sln

# 发布前冒烟（不需要 SDK，对已构建的 exe 直接跑）
.\bin\Release\net9.0-windows\ApiKeyManager.exe --selftest
```

## 八、重新构建

需要 .NET 9 SDK：

```powershell
# 自包含（推荐，免运行时）
dotnet publish .\ApiKeyManager.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o .\dist\standalone

# 精简版（需 .NET 9 Desktop Runtime）
dotnet publish .\ApiKeyManager.csproj -c Release -r win-x64 --self-contained false `
  -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false `
  -o .\dist\net-runtime
```

首次构建后如需更换程序图标：`ApiKeyManager.exe --makeicon icon.ico`，再重新构建（csproj 已配置 `ApplicationIcon`）。

## 九、源码结构

| 文件 | 内容 |
| --- | --- |
| `Program.cs` | 入口（GUI / 自检 / 图标 / 字形校验 / 演示模式分流） |
| `Vault.cs` | 数据模型、AES-GCM 加密存储、设置与路径解析、密钥掩码 |
| `EntryRepository.cs` | 记录集合与落盘、导入合并、覆盖前自动备份 |
| `EntrySearch.cs` | 列表模糊搜索（跨名称/提供商/标签/备注/URL/模型） |
| `ClipboardGuard.cs` | 剪贴板定时清理（不误删他人复制的内容） |
| `CsvExporter.cs` | 明文 CSV 导出（含公式注入防护） |
| `Theme.cs` | 主题调色板（浅色/深色）与自绘控件（圆角按钮、卡片、输入框、徽标、胶囊） |
| `MainForm.cs` | 主界面：列表、搜索、增删改查、复制、导入导出、自动锁定、主题切换 |
| `Forms.cs` | 口令输入框、修改主密码、记录编辑对话框（卡片式） |
| `IconFactory.cs` | 图标生成（ICO 打包）与 TTF cmap 字形校验 |
| `IterationPolicy.cs` | PBKDF2 迭代次数策略（当前推荐值 / 升级阈值 / 可接受区间） |
| `ExpiryPolicy.cs` | 密钥到期判定（已过期 / 即将到期 / 无期限）与提醒汇总 |
| `Demo.cs` | 演示模式示例数据与目录隔离 |
| `SelfTest.cs` | 无界面自检（发布前冒烟） |
| `tests\ApiKeyManager.Tests\` | xUnit 单元测试工程 |

## 十、安全与工程注意事项

- **`vault.akv` / `*.akvbak` / `dist\` 已被 `.gitignore` 排除**，请勿强制加入版本库；即使当前是空库也不应入库。
- 导入「替换」模式是破坏性操作，程序会**先自动把当前库复制为 `vault.akv.<时间戳>.bak`** 再覆盖，出错可回滚。
- 导出加密备份时，`settings.json` 里的界面偏好会一并加密写入备份文件，换机导入即可恢复。
- 导出明文 CSV 时会对 `=`、`+`、`-`、`@`、Tab、CR 开头的字段做转义，避免在 Excel 中被当作公式执行。
- `--demo` 的数据目录重定向在进程生命周期内有效且可通过作用域还原，不会与真实数据互相污染。
- **无障碍**：所有自绘控件都显式声明 `AccessibleRole`；可交互控件（含图标按钮、日期选择器、
  搜索框、自动锁定输入框）均提供 `AccessibleName`；`FieldBox` 把字段标签与占位提示
  同步到内部 `TextBox` 的 `AccessibleName` / `AccessibleDescription`（密码框标注为"机密输入"）；
  纯装饰元素（徽标、卡片）声明为 `AccessibleRole.None` 且不接收焦点。
  这些不变量由 `AccessibilityTests` 强制校验。

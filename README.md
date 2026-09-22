# API Key 管理器（ApiKeyManager）

一个单文件、免安装的 Windows 小工具，用于本地集中管理各家平台的 API Key。
所有数据用主密码加密后存放在本地 `vault.akv`，不联网、不上传。

GUI 使用 **WPF**（`net9.0-windows`），业务逻辑抽成独立的纯 .NET 类库
（**`ApiKeyManager.Core`，目标框架 `net9.0`，不含任何 UI 依赖**）。

## 一、成品

| 形态 | 体积 | 说明 |
| --- | --- | --- |
| 框架依赖单文件 | **0.29 MB** | 需目标机器已装 .NET 9 Desktop Runtime |
| 自包含 | **61.9 MB** | 免安装运行时；WPF 的原生渲染组件无法并入单文件，故为 6 个文件 |

均为 x64 Windows 桌面程序（Windows 10/11）。

```powershell
# 框架依赖（推荐，体积可忽略）
dotnet publish .\src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o .\dist\net-runtime

# 自包含（免运行时）
dotnet publish .\src\ApiKeyManager.Wpf\ApiKeyManager.Wpf.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o .\dist\standalone

# 两种形态一键发布 + 体积统计 + 产物自检
.\tools\verify-publish.ps1
```

> **关于体积**：WPF **不支持 `PublishTrimmed`**，开启会直接报 `NETSDK1168`
> （官方明确说明不支持裁剪 WPF）。因此自包含版的 61.9 MB 已是下限。
> 若在意体积，用框架依赖版（0.29 MB）。
>
> **关于压缩**：`EnableCompressionInSingleFile` 只在自包含发布时可用，
> 框架依赖版开启会得到 `NETSDK1176`。项目文件里已按条件自动设置。

**界面效果**：`shots\list-light.png`（浅色）、`shots\list-dark.png`（深色）、
`shots\30-password-dialog.png`（主密码）、`shots\32-expiry-alert.png`（到期提醒）、
`shots\21-after-save.png`（新增后落库）

## 二、界面

- 卡片式布局：圆角卡片、圆角按钮、Segoe MDL2 图标、程序图标（钥匙）
- **浅色 / 深色双主题**：右上角一键切换，**即时生效且不重建可视树**
  （只替换 `MergedDictionaries[1]`，所有颜色走 `DynamicResource`）
- 8 列表格：名称 / 提供商 / API Key / Base URL / 模型 / 标签 / 到期日 / 更新时间
  - 标签渲染为彩色 chip（最多 3 个，超出显示 `…`）
  - 到期日按语义着色：**已过期=红、14 天内=橙、正常=灰**
  - API Key 与 Base URL 用等宽字体（Cascadia Mono → Consolas 兜底）
  - 长文本 `CharacterEllipsis` 截断，完整值在行 ToolTip 里
- 键盘：`Ctrl+F` 查找、`Ctrl+L` 锁定、`Ctrl+N` 新增、`Ctrl+T` 切换主题
- 状态栏：迭代升级提示、`记录: N / M 条`、到期汇总、`自动锁定 mm:ss` 倒计时

## 三、功能

- **主密码保护**：首次启动设置主密码（≥8 位），全部数据 AES-256-GCM 加密存储
- **记录字段**：名称、提供商/平台、API Key、Base URL、默认模型、标签、备注、**到期日**、创建/更新时间
- **搜索过滤**：按名称 / 提供商 / 标签 / 备注 / URL / 模型即时模糊过滤
- **密钥掩码**：列表默认显示 `sk-1••••••••••••abcd`，可一键「显示密钥 / 隐藏密钥」
- **一键复制**：复制 Key / 复制 Base URL；复制的内容 **30 秒后自动清除剪贴板**（可配）
- **到期提醒**：可为每个 Key 设置到期日；列表中「到期日」列语义着色，
  解锁时弹一次主题化汇总提醒；勾「不提醒」即退出该机制
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
| `--demo` | 演示模式：生成 6 条示例数据并自动解锁（不碰真实数据） |
| `--dir <路径>` | 重定向数据目录。**优先于 `--demo`**，便于脚本把演示数据写到可预期的位置 |
| `--dark` | 以深色主题启动 |
| `--makeicon <路径>` | 生成程序图标 `icon.ico`（构建用） |
| `--glyphcheck` | 校验按钮图标字形在 Segoe MDL2 中的覆盖（构建用） |
| `--layoutcheck` | 离屏强制排版一次，把各 Grid 的实测列宽写入 `%TEMP%\akm-layoutcheck.txt`（排查布局用） |
| `--dialogcheck` | 对 8 种对话框模式逐一离屏排版，断言所有按钮都完整落在窗口内（自检按钮被裁） |

> `--demo` 会把数据目录整体重定向，并且**该重定向在整个进程存活期间有效**——
> `--demo --dark` 之类的组合只会改演示目录里的 `settings.json`，不会污染你真实的偏好设置。
> 同时给了 `--dir` 时以 `--dir` 为准。

## 七、测试与开发

```powershell
# 单元测试（需要 .NET 9 SDK）；135 项，覆盖加密往返 / 防篡改 / 迭代升级 / 到期判定 /
# CSV 注入 / 剪贴板清理 / 目录解析 / 主题键一致性 / 无障碍元数据 / 对话框尺寸不变量
dotnet test .\ApiKeyManager.sln

# 发布前冒烟（不需要 SDK，对已构建的 exe 直接跑）
.\src\ApiKeyManager.Wpf\bin\Debug\net9.0-windows\ApiKeyManager.exe --selftest

# GUI 实机验证（自动解锁 → 关掉提醒 → 截浅色/深色两张图）
.\tools\ui-verify.ps1 -ExePath <exe> -OutDir .\shots
```

## 八、源码结构

```
src\ApiKeyManager.Core\        纯 .NET 类库，net9.0，零 UI 依赖
  Vault.cs                     数据模型、AES-GCM 加密存储、路径解析、密钥掩码
  EntryRepository.cs           记录集合与落盘、导入合并、覆盖前自动备份
  EntrySearch.cs               列表模糊搜索
  ClipboardGuard.cs            剪贴板定时清理（读/清通过委托注入，保持 UI 无关）
  CsvExporter.cs               明文 CSV 导出（公式注入防护）
  IterationPolicy.cs           PBKDF2 迭代策略（推荐值 / 升级阈值 / 可接受区间）
  ExpiryPolicy.cs              到期判定（已过期 / 即将到期 / 无期限）与提醒汇总

src\ApiKeyManager.Wpf\         WPF 外壳，net9.0-windows
  App.xaml.cs                  入口与命令行分流
  Themes\Metrics.xaml          尺寸令牌（间距 / 圆角 / 字号 / 行高）
  Themes\Light.xaml            浅色调色板
  Themes\Dark.xaml             深色调色板
  Themes\Controls.xaml         控件样式（按钮 / 输入框 / 卡片 / chip / 表头 / 单元格）
  Themes\ThemeManager.cs       主题切换（只换字典，不重建可视树）
  ViewModels\                  MVVM：ViewModelBase / RelayCommand / EntryViewModel / MainViewModel
  Services\                    IDialogService 接缝 + WPF 实现、自动锁定监听、剪贴板服务
  Views\                       MainWindow 与 4 个对话框（密码 / 编辑 / 导入方式 / 消息框）
  Converters\Converters.cs     到期色 / 布尔取反 / 可见性转换器
  GlyphCatalog.cs              字形覆盖自检

tests\ApiKeyManager.Tests\     xUnit 测试工程（只引用 Core）
tools\                         截图与实机验证脚本
```

### 架构要点

- **Core 目标框架是 `net9.0`（不是 `net9.0-windows`）**，这不是随意的选择：
  编译器会**直接拒绝**任何 `System.Windows` / `System.Windows.Forms` 引用。
  "业务逻辑不依赖 UI"因此从约定升级为编译期保证。
- **主题切换不重建可视树**：所有颜色都用 `DynamicResource`，
  切换时只替换 `MergedDictionaries[1]`，界面元素保持不变（无闪烁、无状态丢失）。
- **`Light.xaml` 与 `Dark.xaml` 的键必须完全一致**：缺键不会报错，只会静默失去样式。
  这条由 `ThemeParityTests` 强制校验（本轮就靠它抓出过 `ColorExpiry*Text` 缺失）。
- **`IDialogService` 是唯一的平台接缝**：`MainViewModel` 不出现任何 WPF 类型，因此可单测。

## 九、工程注意事项

- **`vault.akv` / `*.akvbak` / `dist\` / `publish-test\` 已被 `.gitignore` 排除**，
  请勿强制加入版本库；即使当前是空库也不应入库。
- 导入「替换」模式是破坏性操作，程序会**先自动把当前库复制为 `vault.akv.<时间戳>.bak`** 再覆盖，出错可回滚。
- 导出加密备份时，`settings.json` 里的界面偏好会一并加密写入备份文件，换机导入即可恢复。
- 导出明文 CSV 时会对 `=`、`+`、`-`、`@`、Tab、CR 开头的字段做转义，避免在 Excel 中被当作公式执行。
- **表格列宽用固定像素**，不用 `*` + `SharedSizeGroup`：
  `SharedSizeGroup` 的语义是"列宽 = 组内最大 DesiredWidth"，会覆盖按比例分配，
  实测把列撑到 1500+ DIP 导致右侧三列跑到窗口外。8 列合计 1000，窗口 1600 有充足余量。
- **对话框高度不要写死，用 `SizeToContent="Height"` 由内容决定**：
  写死高度在字段增删或文案变长后会裁掉底部按钮（曾出现「确定」只露一截、
  用户点不到）。宽度仍由 `Width` 硬约束，`*` 列会正常收缩。
- **主按钮必须放在 `ScrollViewer` 外面**：若 `ScrollViewer` 包住含按钮的整块内容，
  内容超高时按钮会跟着滚出可视区。正确结构是"可滚动字段 + 固定底部按钮"。
- **对话框根元素用 `Grid` 而非 `StackPanel`**：垂直 `StackPanel` 以无限宽测量子项，
  内层 `Grid` 的 `*` 列会撑到内容自然宽度而不收缩。
- **`StackPanel` 以无限宽测量子项**：垂直 `StackPanel` 里放带 `*` 列的 `Grid`，
  该列会展开到内容自然宽度。需要约束宽度时用 `Grid` 的 `Auto` 行代替。
- **`MessageBox` 已全部替换为 `MessageDialog`**：前者是 Win32 对话框，用系统配色，
  不跟随应用主题，深色模式下会弹出一个刺眼的亮色窗口。

### 无障碍

- 自绘 / 纯图标控件（无 `Content` 文字）显式声明 `AutomationProperties.Name`
- 列表与搜索框提供无障碍名称，读屏软件可报出用途
- 查找走 `ApplicationCommands.Find` 标准命令，辅助技术能识别出"本窗口提供查找功能"
- 可交互控件不被移出 Tab 顺序
- 以上不变量由 `AccessibilityTests` 解析 XAML 强制校验
  （比实例化控件更稳：不需要 STA 线程、不启动 `Application`、不受资源字典加载顺序影响）

### 截图工具

`tools\shot-helper.ps1` 用 `DWMWA_EXTENDED_FRAME_BOUNDS` 取窗口的**物理**矩形。

这一步很关键：在高 DPI 显示器上 `GetWindowRect` 返回的是 **DPI 虚拟化**坐标。
实测 `Width=520` 的窗口在 146% 缩放下实际渲染 **762 px** 宽，
但 `GetWindowRect` 仍报 520 —— 按它截图会裁掉右侧，让完全正确的布局看起来像被裁了。
（该窗口的实测数据：virtual 520×540 / physical 762×801。）

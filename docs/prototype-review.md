# 阶段 1：可操作的 WPF 设计原型

本页保留阶段 1 的历史对照。用户已选择路线 B「温润档案风」，生产新版已实现；路线 A「精密工具风」仍可在历史原型中对照。两者均可在原型底栏切换。

## 查看与试用

在项目根目录运行：

```powershell
.\tools\preview-frontend.ps1 -Prototype
```

已构建时可直接运行：

```powershell
.\src\ApiKeyManager.Wpf\bin\Debug\net9.0-windows\ApiKeyManager.exe --prototype
```

原型窗口标题包含“设计原型”，底部显示“仅示例数据”。**不要省略 `--prototype`**，不带参数会打开原有正式程序。原型分支在任何数据路径解析前进入，不读取真实库或用户设置。

建议依次试用：搜索 `claude`、切换到期筛选、点选记录、编辑后取消、编辑后保存预览、切换主题、缩窄窗口、锁定，再用演示密码 `demo` 解锁。

确认范围是布局、视觉、操作位置和阅读体验。真实保存、真实复制、导入备份与系统自动锁定留给后续已批准阶段；原型不会把这些模拟行为标为正式业务成功。

## 画面

| 画面 | 预览 |
| --- | --- |
| A · 精密工具，浅色 | [查看](../artifacts/prototype-review/01-precision-light.png) |
| A · 精密工具，深色 | [查看](../artifacts/prototype-review/02-precision-dark.png) |
| B · 温润档案，浅色 | [查看](../artifacts/prototype-review/03-archive-light.png) |
| 搜索无结果 | [查看](../artifacts/prototype-review/04-no-results.png) |
| 复制反馈（模拟） | [查看](../artifacts/prototype-review/05-copy-feedback.png) |
| 编辑记录 | [查看](../artifacts/prototype-review/06-edit.png) |
| 锁定页 | [查看](../artifacts/prototype-review/07-locked.png) |
| 窄窗口 | [查看](../artifacts/prototype-review/08-narrow.png) |
| 窄窗口编辑与固定按钮 | [查看](../artifacts/prototype-review/09-narrow-editor.png) |
| 宽窗口 | [查看](../artifacts/prototype-review/10-wide.png) |
| 两倍分辨率渲染 | [查看](../artifacts/prototype-review/11-render-200-percent.png) |

## 验证边界

`tools/preview-frontend.ps1 -Check` 在屏幕外创建真实 WPF 窗口，执行控件事件、布局和渲染检查。它不操作系统鼠标键盘，不证明实际显示缩放、多显示器、读屏和用户接受度；两倍位图不是系统 200% 缩放的实测。

检查结果位于 [checks.json](../artifacts/prototype-review/checks.json)，截图为派生文件，可通过命令重新生成，不纳入源代码提交。所有记录和密码为公开合成示例。

原型刻意使用隔离的视图代码便于评审；这不改变目标架构。设计确认后将页面状态迁到正式 ViewModel，将保存和会话交给 Application，再逐项补齐完整功能。

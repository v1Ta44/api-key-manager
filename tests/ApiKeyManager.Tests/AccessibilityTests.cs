using System;
using System.Linq;
using System.Windows.Forms;
using ApiKeyManager;
using Xunit;

namespace ApiKeyManager.Tests;

/// <summary>
/// 无障碍元数据回归测试。
///
/// 自绘控件（ThemedButton / GlyphBadge / PillLabel）不设 AccessibleName 时
/// 读屏软件只会念"控件"或直接跳过，等于该按钮不可用。
/// 这些断言把"每个交互控件都必须有可朗读名称"固定成不变量。
///
/// WinForms 控件要求 STA 线程，xUnit 默认池线程不是 STA，
/// 所以这里用显式的 STA 线程跑。
/// </summary>
public sealed class AccessibilityTests
{
    /// <summary>在 STA 线程上执行 UI 操作。</summary>
    private static void Sta(Action action)
    {
        Exception? captured = null;
        var t = new System.Threading.Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        t.SetApartmentState(System.Threading.ApartmentState.STA);
        t.Start();
        t.Join(TimeSpan.FromSeconds(30));
        if (captured != null) throw captured;
    }

    [Fact]
    public void ThemedButton_ExposesItsTextAsAccessibleName()
    {
        Sta(() =>
        {
            using var btn = new ThemedButton { Text = "新增", Glyph = Theme.IcoAdd };

            Assert.Equal(AccessibleRole.PushButton, btn.AccessibleRole);
            Assert.Equal("新增", btn.AccessibleName);
        });
    }

    [Fact]
    public void ThemedButton_AnyTextChangeUpdatesAccessibleName()
    {
        Sta(() =>
        {
            using var btn = new ThemedButton { Text = "显示密钥" };
            Assert.Equal("显示密钥", btn.AccessibleName);

            btn.Text = "隐藏密钥";
            Assert.Equal("隐藏密钥", btn.AccessibleName);
        });
    }

    [Fact]
    public void ThemedButton_IconOnlyButtonStillHasAName()
    {
        Sta(() =>
        {
            // 只有字形没有文字的按钮不能让读屏读到空名
            using var btn = new ThemedButton { Text = "" };

            Assert.False(string.IsNullOrWhiteSpace(btn.AccessibleName));
        });
    }

    [Fact]
    public void GlyphBadge_IsHiddenFromScreenReaders()
    {
        Sta(() =>
        {
            // 私有区字形读出来是乱码，必须声明为 None 且不接收焦点
            using var badge = new GlyphBadge { Glyph = Theme.IcoLock };

            Assert.Equal(AccessibleRole.None, badge.AccessibleRole);
            Assert.False(badge.TabStop);
        });
    }

    [Fact]
    public void PillLabel_IsReadableStaticText()
    {
        Sta(() =>
        {
            using var pill = new PillLabel { Text = "自动锁定 04:59" };

            Assert.Equal(AccessibleRole.StaticText, pill.AccessibleRole);
            Assert.Equal("自动锁定 04:59", pill.AccessibleName);
            Assert.False(pill.TabStop);
        });
    }

    [Fact]
    public void Card_IsNotAFocusTarget()
    {
        Sta(() =>
        {
            using var card = new Card();

            Assert.Equal(AccessibleRole.None, card.AccessibleRole);
            Assert.False(card.TabStop);
        });
    }

    [Fact]
    public void FieldBox_LabelBecomesInnerTextBoxAccessibleName()
    {
        Sta(() =>
        {
            // 焦点实际落在内部 TextBox 上，名字必须装在那里才有用
            using var box = new FieldBox();
            box.Label = "API Key";

            Assert.Equal("API Key", box.Input.AccessibleName);
        });
    }

    [Fact]
    public void FieldBox_IsNotItselfAFocusTarget()
    {
        Sta(() =>
        {
            using var box = new FieldBox();

            Assert.False(box.TabStop);
            Assert.Equal(AccessibleRole.None, box.AccessibleRole);
        });
    }

    [Fact]
    public void FieldBox_PlaceholderBecomesAccessibleDescription()
    {
        Sta(() =>
        {
            using var box = new FieldBox();
            box.Placeholder = "https://...";

            // 占位符对读屏不可见，必须额外暴露为描述
            Assert.Equal("https://...", box.Input.PlaceholderText);
            Assert.Equal("https://...", box.Input.AccessibleDescription);
        });
    }

    [Fact]
    public void FieldBox_SecretInputIsMarkedAsSuch()
    {
        Sta(() =>
        {
            using var box = new FieldBox();
            box.Label = "密码";
            box.IsSecret = true;

            Assert.True(box.Input.UseSystemPasswordChar);
            Assert.Equal("机密输入", box.Input.AccessibleDescription);

            box.IsSecret = false;
            Assert.False(box.Input.UseSystemPasswordChar);
        });
    }

    [Fact]
    public void FieldBox_SecretMarkedBeatsPlaceholderDescription()
    {
        Sta(() =>
        {
            using var box = new FieldBox();
            box.Placeholder = "密码";
            box.IsSecret = true;

            // 机密输入优先：不能因为设了占位符就把密码框标成普通字段
            Assert.Equal("机密输入", box.Input.AccessibleDescription);
        });
    }

    [Fact]
    public void EveryInteractiveControlInEditDialogHasAName()
    {
        Sta(() =>
        {
            // 端到端：打开真实的编辑对话框，遍历所有可聚焦控件，
            // 要求每一个都有可朗读名称。
            var entry = new ApiEntry { Name = "测试", ApiKey = "sk-1", ExpiresUtc = DateTime.Today };

            using var dlg = CreateEntryDialog(entry);

            var unnamed = Walk(dlg)
                .Where(c => c.TabStop)
                .Where(c => string.IsNullOrWhiteSpace(c.AccessibleName))
                .Select(c => c.GetType().Name)
                .ToList();

            Assert.True(unnamed.Count == 0,
                "以下可聚焦控件缺少 AccessibleName：" + string.Join(", ", unnamed));
        });
    }

    private static Form CreateEntryDialog(ApiEntry entry)
    {
        // EntryEditForm 的构造是私有的，通过公开的 Edit 入口无法在无消息循环下取得实例。
        // 这里用反射构造，仅为测试无障碍元数据。
        var type = typeof(EntryEditForm);
        var ctor = type.GetConstructor(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(ApiEntry), typeof(bool) }, null)!;

        return (Form)ctor.Invoke(new object[] { entry, true });
    }

    private static System.Collections.Generic.IEnumerable<Control> Walk(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in Walk(c)) yield return child;
        }
    }
}

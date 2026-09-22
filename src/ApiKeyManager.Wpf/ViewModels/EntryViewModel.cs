using System;
using System.Collections.Generic;
using System.Linq;
using ApiKeyManager;

namespace ApiKeyManager.Wpf.ViewModels;

/// <summary>
/// 列表中的一行。包装 <see cref="ApiEntry"/>，并额外暴露"视图需要但模型不该关心"的东西：
///   - 掩码后的 Key 文本
///   - 到期状态与对应颜色（原 WinForms 版靠 cell.Style.ForeColor 硬编码）
///   - 拆好的标签数组（原版只能显示 "AI,官方" 纯文本，现在渲染成彩色 chip）
///
/// 注意：本类不引用任何 WPF 类型，颜色只暴露"状态枚举"，由 XAML 用 DataTrigger 决定画刷。
/// 这样颜色仍然完全由主题字典控制，深色主题下不会残留写死的浅色。
/// </summary>
public sealed class EntryViewModel : ViewModels.ViewModelBase
{
    private readonly ApiEntry _entry;
    private bool _showKey;

    public EntryViewModel(ApiEntry entry)
    {
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    /// <summary>底层模型。命令处理时需要的原始数据从这里取。</summary>
    public ApiEntry Model => _entry;

    public string Id => _entry.Id;
    public string Name => _entry.Name;
    public string Provider => _entry.Provider;
    public string BaseUrl => _entry.BaseUrl;
    public string Model2 => _entry.Model;
    public string Notes => _entry.Notes;

    /// <summary>Key 的显示文本：按当前"显示/隐藏密钥"状态决定掩码或明文。</summary>
    public string ApiKeyDisplay => _showKey ? _entry.ApiKey : KeyMask.Format(_entry.ApiKey);

    public string UpdatedDisplay =>
        _entry.UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public string ExpiresDisplay =>
        _entry.ExpiresUtc is { } d ? d.ToString("yyyy-MM-dd") : "—";

    /// <summary>标签列表（用于 ItemsControl 渲染 chip）。空标签不产生元素。</summary>
    public IReadOnlyList<string> TagList =>
        string.IsNullOrWhiteSpace(_entry.Tags)
            ? Array.Empty<string>()
            : _entry.Tags
                .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Take(3)   // 单行最多 3 个 chip，超出会被 "…" 提示
                .ToList();

    /// <summary>标签被截断时的提示（多余几个）。</summary>
    public int HiddenTagCount
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_entry.Tags)) return 0;
            int total = _entry.Tags
                .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries)
                .Count(t => t.Trim().Length > 0);
            return Math.Max(0, total - 3);
        }
    }

    public bool HasHiddenTags => HiddenTagCount > 0;

    /// <summary>到期状态（视图用 DataTrigger 映射到颜色）。</summary>
    public ExpiryState ExpiryState => ExpiryPolicy.Evaluate(_entry, DateTime.Now).State;

    public string ExpiryTooltip
    {
        get
        {
            var info = ExpiryPolicy.Evaluate(_entry, DateTime.Now);
            return info.State == ExpiryState.None
                ? $"{Name}：未设置到期日"
                : $"{Name}：{info.Describe()}";
        }
    }

    /// <summary>整行 tooltip：把被列宽截断的信息补全。</summary>
    public string RowTooltip
    {
        get
        {
            var parts = new List<string> { Name };
            if (!string.IsNullOrWhiteSpace(Provider)) parts.Add("提供商：" + Provider);
            if (!string.IsNullOrWhiteSpace(BaseUrl)) parts.Add("Base URL：" + BaseUrl);
            if (!string.IsNullOrWhiteSpace(Model2)) parts.Add("模型：" + Model2);
            if (!string.IsNullOrWhiteSpace(_entry.Tags)) parts.Add("标签：" + _entry.Tags);
            if (!string.IsNullOrWhiteSpace(Notes)) parts.Add("备注：" + Notes);
            var exp = ExpiryPolicy.Evaluate(_entry, DateTime.Now);
            if (exp.State != ExpiryState.None) parts.Add("到期：" + exp.Describe());
            return string.Join(Environment.NewLine, parts);
        }
    }

    /// <summary>切换密钥掩码显示。</summary>
    public void SetShowKey(bool show)
    {
        if (_showKey == show) return;
        _showKey = show;
        OnPropertyChanged(nameof(ApiKeyDisplay));
    }

    /// <summary>
    /// 底层模型被外部修改后（编辑对话框返回）刷新全部只读投影。
    /// </summary>
    public void RefreshAll() => RaiseAll(
        nameof(Name), nameof(Provider), nameof(BaseUrl), nameof(Model2), nameof(Notes),
        nameof(ApiKeyDisplay), nameof(UpdatedDisplay), nameof(ExpiresDisplay),
        nameof(TagList), nameof(HiddenTagCount), nameof(HasHiddenTags),
        nameof(ExpiryState), nameof(ExpiryTooltip), nameof(RowTooltip));
}

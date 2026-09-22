using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ApiKeyManager.Wpf.ViewModels;

/// <summary>
/// MVVM 基类：实现 INotifyPropertyChanged，并提供 SetField 辅助方法。
///
/// 为什么必须做对：WPF 的绑定错误是运行时静默失败 ——
/// 属性名拼错、忘记触发通知，界面就是不更新且不报错。
/// 所有属性一律通过 SetField 赋值，可以杜绝"忘了 OnPropertyChanged"。
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// 赋值并在值确实变化时触发通知。返回值表示是否发生了变化。
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    /// <summary>批量刷新多个属性的通知（用于"整体重算"场景）。</summary>
    protected void RaiseAll(params string[] names)
    {
        foreach (string n in names) OnPropertyChanged(n);
    }

    /// <summary>
    /// 对外暴露的单属性通知。计算属性（只读、由其它状态推导）在这条链上
    /// 需要由外部在源头变化后主动触发，否则界面不会更新。
    /// </summary>
    public void Raise(string name) => OnPropertyChanged(name);
}

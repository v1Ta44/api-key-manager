using System;

namespace ApiKeyManager;

/// <summary>
/// PBKDF2 迭代次数策略。
///
/// 为什么需要它：迭代次数写在库文件头里（格式版本 1 起就有），
/// 所以老用户用旧机器上创建的库不会因为程序升级而自动变强——
/// 密码派生强度一旦定下就永久停留在建库那一刻的值。
/// 这里定义"当前推荐值"和"低于它就应静默升级"的阈值，
/// 解锁成功后由 MainForm 触发一次静默重加密。
///
/// 数值参考 OWASP Password Storage Cheat Sheet 对 PBKDF2-HMAC-SHA256 的建议
/// （600 000 次作为下限）。上调这个常量不需要改文件格式：
/// 新库用新值写，老库在下次解锁时自动跟上。
/// </summary>
public static class IterationPolicy
{
    /// <summary>新库使用的迭代次数。</summary>
    public const int Current = 600_000;

    /// <summary>
    /// 低于此值即认为"明显过弱"，必须升级。
    /// 取 <see cref="Current"/> 的一半：既避免每次发布都白白重写用户库，
    /// 又能保证强度落后一代以上时一定被拉齐。
    /// </summary>
    public const int UpgradeThreshold = Current / 2;

    /// <summary>
    /// 解析库文件头时接受的迭代次数下限。
    /// 故意放得比 <see cref="UpgradeThreshold"/> 低：这样很老的库仍然能打开
    /// （打开后会被升级），而不是直接报"头已损坏"把用户锁在门外。
    /// </summary>
    public const int MinAcceptable = 1_000;

    /// <summary>解析库文件头时接受的迭代次数上限，防止构造恶意头造成超长计算。</summary>
    public const int MaxAcceptable = 10_000_000;

    /// <summary>该迭代次数是否需要升级到 <see cref="Current"/>。</summary>
    public static bool NeedsUpgrade(int iterations) => iterations < UpgradeThreshold;

    /// <summary>该迭代次数是否在可解析范围内。</summary>
    public static bool IsAcceptable(int iterations) =>
        iterations >= MinAcceptable && iterations <= MaxAcceptable;

    /// <summary>
    /// 给用户看的强度描述。仅用于提示文案，不参与任何安全判定。
    /// </summary>
    public static string Describe(int iterations) => iterations switch
    {
        < 100_000 => "偏弱",
        < UpgradeThreshold => "一般",
        < Current => "良好",
        _ => "强",
    };
}

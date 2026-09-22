using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ApiKeyManager;

// ---------------- 数据模型 ----------------

public sealed class ApiEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string Model { get; set; } = "";
    public string Tags { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 密钥到期时间（本地时间），null 表示不设置提醒。
    /// 用 DateTime? 而非"永不过期"的哨兵值，这样老的库文件里没有该字段时
    /// 反序列化结果就是"未设置"，语义天然一致。
    /// </summary>
    public DateTime? ExpiresUtc { get; set; }

    public ApiEntry Clone() => (ApiEntry)MemberwiseClone();
}

public sealed class VaultData
{
    public List<ApiEntry> Entries { get; set; } = new();

    /// <summary>
    /// 备份文件里一并携带的界面偏好。库文件本身不带（偏好是明文存 settings.json 的），
    /// 所以这里可空：旧的备份文件没有这个字段，反序列化后为 null。
    /// </summary>
    public AppSettings? Settings { get; set; }
}

public sealed class VaultException : Exception
{
    public VaultException(string message) : base(message) { }
}

/// <summary>
/// 不解密就能读到的库文件头信息（迭代数与格式版本都在未认证的明文头里）。
/// </summary>
public readonly record struct VaultMetadata(int FormatVersion, int Iterations)
{
    /// <summary>按当前策略是否需要重新加密。</summary>
    public bool NeedsUpgrade => IterationPolicy.NeedsUpgrade(Iterations);

    /// <summary>强度描述（"偏弱"/"一般"/"良好"/"强"）。</summary>
    public string Strength => IterationPolicy.Describe(Iterations);
}

// ---------------- 加密存储 ----------------
//
// 文件格式 vault.akv / *.akvbak（小端）：
//   [0..8)   magic  "AKVLT001"
//   [8]      格式版本 = 1
//   [9..13)  PBKDF2 迭代次数 (int32)
//   [13..29) 盐 (16 字节)
//   [29..41) AES-GCM nonce (12 字节)
//   [41..57) GCM 认证标签 (16 字节)
//   [57..]   密文（JSON：VaultData）
// 头部 [0..41) 作为 AAD 参与认证，任何头部或密文篡改都会导致解密失败。
//
// 迭代次数在明文头里（必须如此：派生口令要先知道迭代次数）。
// 它虽然受 AAD 保护不会被静默改写，但攻击者可以读到一个"强度提示"。
// 这是该格式的已知取舍，不影响机密性。
//
// 升级通道：新库一律用 IterationPolicy.Current 写；
// 解锁成功后若发现库里的值明显偏低，用新值原地重加密（见 MainForm.MaybeUpgradeIterations）。

public static class VaultStore
{
    /// <summary>新库使用的迭代次数。保留此名字是为了兼容既有调用点。</summary>
    public const int DefaultIterations = IterationPolicy.Current;

    private const byte FormatVersion = 1;
    private const int SaltLen = 16;
    private const int NonceLen = 12;
    private const int TagLen = 16;
    private const int HeaderLen = 8 + 1 + 4 + SaltLen + NonceLen; // 41

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AKVLT001");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iterations, HashAlgorithmName.SHA256, 32);

    public static void Save(string path, VaultData data, string password, int iterations = DefaultIterations)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltLen);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceLen);
        byte[] header = BuildHeader(iterations, salt, nonce);
        byte[] key = DeriveKey(password, salt, iterations);
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagLen];

        using (var gcm = new AesGcm(key, TagLen))
        {
            gcm.Encrypt(nonce, plain, cipher, tag, header);
        }

        byte[] file = new byte[HeaderLen + TagLen + cipher.Length];
        Buffer.BlockCopy(header, 0, file, 0, HeaderLen);
        Buffer.BlockCopy(tag, 0, file, HeaderLen, TagLen);
        Buffer.BlockCopy(cipher, 0, file, HeaderLen + TagLen, cipher.Length);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 原子写入：先写临时文件再替换，避免写一半损坏库文件
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(tmp, file);
        File.Move(tmp, path, true);
    }

    public static VaultData Load(string path, string password) => Parse(File.ReadAllBytes(path), password);

    /// <summary>
    /// 只读取库文件头（不解密、不需要口令）。文件不存在或不是有效库时返回 null。
    /// 用于在解锁前就知道迭代数，进而决定是否需要升级。
    /// </summary>
    public static VaultMetadata? Inspect(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);

            byte[] head = new byte[HeaderLen];
            if (fs.ReadAtLeast(head, HeaderLen, throwOnEndOfStream: false) < HeaderLen) return null;

            for (int i = 0; i < Magic.Length; i++)
            {
                if (head[i] != Magic[i]) return null;
            }

            byte version = head[8];
            if (version != FormatVersion) return null;

            int iterations = BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(9, 4));
            if (!IterationPolicy.IsAcceptable(iterations)) return null;

            return new VaultMetadata(version, iterations);
        }
        catch
        {
            // 读取失败（占用 / 权限 / IO）不该影响启动流程，按"未知"处理
            return null;
        }
    }

    public static VaultData Parse(byte[] file, string password)
    {
        if (file.Length < HeaderLen + TagLen)
            throw new VaultException("库文件已损坏或格式不正确。");

        for (int i = 0; i < Magic.Length; i++)
        {
            if (file[i] != Magic[i])
                throw new VaultException("这不是有效的 API Key 库文件。");
        }

        if (file[8] != FormatVersion)
            throw new VaultException($"不支持的库文件版本（{file[8]}）。");

        int iterations = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(9, 4));
        if (!IterationPolicy.IsAcceptable(iterations))
            throw new VaultException("库文件头已损坏。");

        byte[] header = file[..HeaderLen];
        byte[] salt = file.AsSpan(13, SaltLen).ToArray();
        byte[] nonce = file.AsSpan(29, NonceLen).ToArray();
        byte[] tag = file.AsSpan(HeaderLen, TagLen).ToArray();
        byte[] cipher = file.AsSpan(HeaderLen + TagLen).ToArray();

        byte[] key = DeriveKey(password, salt, iterations);
        byte[] plain = new byte[cipher.Length];

        try
        {
            using var gcm = new AesGcm(key, TagLen);
            gcm.Decrypt(nonce, cipher, tag, plain, header);
        }
        catch (CryptographicException)
        {
            throw new VaultException("主密码错误，或库文件已被篡改 / 损坏。");
        }

        try
        {
            return JsonSerializer.Deserialize<VaultData>(plain) ?? new VaultData();
        }
        catch (JsonException)
        {
            throw new VaultException("库文件内容无法解析。");
        }
    }

    private static byte[] BuildHeader(int iterations, byte[] salt, byte[] nonce)
    {
        byte[] header = new byte[HeaderLen];
        Buffer.BlockCopy(Magic, 0, header, 0, Magic.Length);
        header[8] = FormatVersion;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), iterations);
        Buffer.BlockCopy(salt, 0, header, 13, SaltLen);
        Buffer.BlockCopy(nonce, 0, header, 29, NonceLen);
        return header;
    }
}

// ---------------- 设置与路径 ----------------

public sealed class AppSettings
{
    public int AutoLockMinutes { get; set; } = 5;
    public int ClipboardClearSeconds { get; set; } = 30;
    public bool ShowKeys { get; set; }
    public string Theme { get; set; } = "Light";
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings Load(string dir)
    {
        try
        {
            string path = Path.Combine(dir, "settings.json");
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(string dir, AppSettings settings) => TrySave(dir, settings);

    public static bool TrySave(string dir, AppSettings settings)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(dir);
            temp = Path.Combine(dir, "settings." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, Path.Combine(dir, "settings.json"), true);
            return true;
        }
        catch { return false; }
        finally { try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { } }
    }

}

public static class DataPaths
{
    private static string? _overrideDir;

    /// <summary>当前是否处于目录覆盖状态（演示模式）。</summary>
    public static bool IsOverridden => _overrideDir != null;

    /// <summary>
    /// 强制使用指定目录（演示模式）。返回可释放的作用域，
    /// 释放后恢复为按 exe 目录 / %APPDATA% 自动解析。
    /// </summary>
    public static IDisposable UseDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        _overrideDir = dir;
        return new Scope(() => _overrideDir = null);
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }

    /// <summary>优先使用 exe 所在目录（便携模式）；不可写时回退到 %APPDATA%\ApiKeyManager。</summary>
    public static (string DataDir, string VaultPath) Resolve()
    {
        if (_overrideDir != null)
            return (_overrideDir, Path.Combine(_overrideDir, "vault.akv"));

        string exePath = Environment.ProcessPath ?? "";
        string exeDir = string.IsNullOrEmpty(exePath)
            ? AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : Path.GetDirectoryName(exePath)!;

        if (IsWritable(exeDir))
            return (exeDir, Path.Combine(exeDir, "vault.akv"));

        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApiKeyManager");
        Directory.CreateDirectory(appData);
        return (appData, Path.Combine(appData, "vault.akv"));
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".akm-probe-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// ---------------- 密钥掩码 ----------------

public static class KeyMask
{
    public static string Format(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        string k = key.Trim();
        if (k.Length <= 8) return new string('•', Math.Max(4, k.Length));
        return k[..4] + new string('•', 10) + k[^4..];
    }
}

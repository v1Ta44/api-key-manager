using ApiKeyManager;

namespace ApiKeyManager.Application;

public interface IVaultStorage
{
    bool Exists { get; }
    VaultData Load(string password);
    void Save(VaultData data, string password);
    VaultMetadata? Inspect();
    string Backup();
}

public sealed class FileVaultStorage(string path) : IVaultStorage
{
    public string PathName => Path.GetFullPath(path);
    public bool Exists => File.Exists(path);
    public VaultData Load(string password) => VaultStore.Load(path, password);
    public void Save(VaultData data, string password)
    {
        var candidate = path + ".candidate-" + Guid.NewGuid().ToString("N");
        try
        {
            VaultStore.Save(candidate, data, password);
            var verified = VaultStore.Load(candidate, password);
            if (System.Text.Json.JsonSerializer.Serialize(data) != System.Text.Json.JsonSerializer.Serialize(verified))
                throw new IOException("写入校验失败。");
            File.Move(candidate, path, true);
        }
        finally { if (File.Exists(candidate)) File.Delete(candidate); }
    }
    public VaultMetadata? Inspect() => VaultStore.Inspect(path);
    public string Backup()
    {
        var backup = path + "." + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".bak";
        File.Copy(path, backup, false);
        return backup;
    }
}

/// <summary>仅供隔离界面原型使用，不访问磁盘。</summary>
public sealed class MemoryVaultStorage(VaultData initial, string initialPassword = "demo") : IVaultStorage
{
    private VaultData _data = Clone(initial);
    private string _password = initialPassword;
    public bool Exists => true;
    public VaultData Load(string password) => password == _password ? Clone(_data) : throw new VaultException("密码不正确。");
    public void Save(VaultData data, string password) { _data = Clone(data); _password = password; }
    public VaultMetadata? Inspect() => new(1, IterationPolicy.Current);
    public string Backup() => "内存演示副本";
    private static VaultData Clone(VaultData data) => new() { Entries = data.Entries.Select(x => x.Clone()).ToList() };
}

/// <summary>同一个库目录的独占句柄，不依赖进程名称，不删除其他进程的锁。</summary>
public sealed class VaultLease : IDisposable
{
    private readonly FileStream _handle;
    public VaultLease(string vaultPath)
    {
        var path = Path.GetFullPath(vaultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _handle = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() => _handle.Dispose();
}

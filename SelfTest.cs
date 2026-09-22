using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ApiKeyManager;

/// <summary>
/// 无界面自检：ApiKeyManager.exe --selftest
/// 这是"发布前冒烟"入口（不需要 SDK 也能在目标机器上跑）。
/// 完整的可回归测试请用 dotnet test（tests\ApiKeyManager.Tests）。
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* 无控制台时忽略 */ }

        var sb = new StringBuilder();
        int failed = 0;

        void Check(string name, bool ok, string detail = "")
        {
            sb.AppendLine($"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? "  — " + detail : "")}");
            if (!ok) failed++;
        }

        const string password = "test-password-123";
        string dir = Path.Combine(Path.GetTempPath(), "akm-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            // 1. PBKDF2 派生稳定、长度正确
            byte[] salt = new byte[16];
            byte[] k1 = VaultStore.DeriveKey(password, salt, 10_000);
            byte[] k2 = VaultStore.DeriveKey(password, salt, 10_000);
            byte[] k3 = VaultStore.DeriveKey(password + "x", salt, 10_000);
            Check("PBKDF2-HMAC-SHA256 派生稳定（32 字节）",
                k1.Length == 32 && k1.SequenceEqual(k2) && !k1.SequenceEqual(k3));

            // 2. 加密保存 → 解密加载往返一致
            string path = Path.Combine(dir, "vault.akv");
            var data = new VaultData
            {
                Entries = new List<ApiEntry>
                {
                    new() { Name = "OpenAI", Provider = "OpenAI", ApiKey = "sk-test-0123456789",
                            BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o", Tags = "ai,官方", Notes = "备注 A" },
                    new() { Name = "DeepSeek", Provider = "DeepSeek", ApiKey = "sk-deep-9876543210",
                            BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", Tags = "ai", Notes = "备注 B" },
                }
            };
            VaultStore.Save(path, data, password, 2_000);
            var loaded = VaultStore.Load(path, password);
            bool same = loaded.Entries.Count == 2
                && loaded.Entries[0].Name == "OpenAI"
                && loaded.Entries[0].ApiKey == "sk-test-0123456789"
                && loaded.Entries[0].BaseUrl == "https://api.openai.com/v1"
                && loaded.Entries[1].Notes == "备注 B";
            Check("加密保存 → 解密加载往返一致（含中文）", same);

            // 3. 错误口令被拒绝
            bool rejected = false;
            try { VaultStore.Load(path, "wrong-password"); }
            catch (VaultException) { rejected = true; }
            Check("错误主密码被拒绝", rejected);

            // 4. 密文被篡改 → 解密失败
            byte[] raw = File.ReadAllBytes(path);
            raw[^1] ^= 0x5A;
            bool tampered = false;
            try { VaultStore.Parse(raw, password); }
            catch (VaultException) { tampered = true; }
            Check("密文被篡改时解密失败（GCM 认证）", tampered);

            // 5. 文件头被篡改 → 解密失败
            byte[] raw2 = File.ReadAllBytes(path);
            raw2[15] ^= 0x01;
            bool headerBad = false;
            try { VaultStore.Parse(raw2, password); }
            catch (VaultException) { headerBad = true; }
            Check("文件头（盐）被篡改时解密失败（AAD 绑定）", headerBad);

            // 6. 非库文件被拒绝
            bool notAVault = false;
            try { VaultStore.Parse(Encoding.UTF8.GetBytes("this is not a vault file at all ........."), password); }
            catch (VaultException) { notAVault = true; }
            Check("非库文件被拒绝", notAVault);

            // 7. 掩码不泄露原文
            string masked = KeyMask.Format("sk-1234567890abcdef");
            Check("密钥掩码不泄露原文且保留尾部",
                masked != "sk-1234567890abcdef" && masked.EndsWith("cdef") && !masked.Contains("1234567890abcdef"));

            // 8. 大量记录往返
            var big = new VaultData();
            for (int i = 0; i < 200; i++)
                big.Entries.Add(new ApiEntry { Name = "服务-" + i, ApiKey = "key-" + i, Notes = "备注 " + i });
            string p2 = Path.Combine(dir, "big.akv");
            VaultStore.Save(p2, big, "another-pass-456", 2_000);
            var bigLoaded = VaultStore.Load(p2, "another-pass-456");
            Check("200 条记录往返一致",
                bigLoaded.Entries.Count == 200 && bigLoaded.Entries[199].Name == "服务-199");

            // 9. 空库往返
            string p3 = Path.Combine(dir, "empty.akv");
            VaultStore.Save(p3, new VaultData(), password, 2_000);
            Check("空库往返一致", VaultStore.Load(p3, password).Entries.Count == 0);
        }
        catch (Exception ex)
        {
            Check("自检执行（异常）", false, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 忽略 */ }
        }

        string report = Path.Combine(Path.GetTempPath(), "api-key-manager-selftest.txt");
        File.WriteAllText(report, sb.ToString(), Encoding.UTF8);

        Console.WriteLine();
        Console.WriteLine("==== API Key 管理器 自检 ====");
        Console.Write(sb.ToString());
        Console.WriteLine(failed == 0 ? "结果：全部通过 (ALL PASS)" : $"结果：{failed} 项失败");
        Console.WriteLine("报告文件：" + report);
        return failed == 0 ? 0 : 1;
    }
}

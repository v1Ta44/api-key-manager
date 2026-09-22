using System;
using System.Collections.Generic;
using System.IO;

namespace ApiKeyManager;

/// <summary>演示模式（--demo）：在临时目录生成示例库并自动解锁，便于查看界面效果。</summary>
internal static class Demo
{
    public const string Password = "demo1234";

    /// <summary>
    /// 演示目录的作用域。必须在整个进程存活期间持有，
    /// 否则 DataPaths 会回落到真实数据目录，导致演示数据写进用户真实库。
    /// </summary>
    private static IDisposable? _scope;

    public static void Prepare()
    {
        string dir = Path.Combine(Path.GetTempPath(), "akm-demo");

        // 先切换到演示目录，再判断库文件是否存在：
        // 顺序反了会去查真实目录。
        _scope = DataPaths.UseDirectory(dir);

        string vault = Path.Combine(dir, "vault.akv");
        if (File.Exists(vault)) return;

        var data = new VaultData
        {
            Entries = new List<ApiEntry>
            {
                new() { Name = "OpenAI", Provider = "OpenAI", ApiKey = "sk-proj-demo0001abcdefghijkl",
                        BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o", Tags = "AI,官方", Notes = "公司账号，月度限额 $500" },
                new() { Name = "DeepSeek", Provider = "DeepSeek", ApiKey = "sk-deepseek-demo0002mnopqrst",
                        BaseUrl = "https://api.deepseek.com", Model = "deepseek-chat", Tags = "AI", Notes = "个人开发用" },
                new() { Name = "Anthropic", Provider = "Anthropic", ApiKey = "sk-ant-demo0003uvwxyz123456",
                        BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-5", Tags = "AI,官方", Notes = "" },
                new() { Name = "Azure OpenAI 东区", Provider = "Microsoft Azure", ApiKey = "a1b2c3d4e5f6demo0004ghij",
                        BaseUrl = "https://TARGET.openai.azure.com", Model = "gpt-4o-mini", Tags = "AI,企业", Notes = "资源名 TARGET，密钥每 90 天轮换",
                        ExpiresUtc = DateTime.Today.AddDays(6) },
                new() { Name = "通义千问", Provider = "阿里云", ApiKey = "sk-demo0005qwen678901234",
                        BaseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1", Model = "qwen-max", Tags = "AI,国内", Notes = "测试环境",
                        ExpiresUtc = DateTime.Today.AddDays(-3) },
                new() { Name = "短信网关", Provider = "云短信", ApiKey = "SMS-demo0006-key-9876543210",
                        BaseUrl = "https://sms.TARGET.com/v2", Model = "", Tags = "短信,生产", Notes = "生产环境通知短信",
                        ExpiresUtc = DateTime.Today.AddMonths(4) },
            }
        };

        VaultStore.Save(vault, data, Password);
    }
}

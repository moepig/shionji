using System.Text;

internal static class FakeAwsLogin
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine($"PID:{Environment.ProcessId}");
        if (args.Contains("sensitive-output"))
        {
            // 形式・改行・出力ストリームに関係なくログへ漏れないことを検証する。
            Console.WriteLine("https://ap-northeast-1.signin.aws.amazon.com/v1/authorize?state=DO_NOT_LOG_STATE&code_challenge=DO_NOT_LOG_CHALLENGE");
            Console.WriteLine("{\n  \"accessToken\": \"DO_NOT_LOG_ACCESS_TOKEN\",\n  \"refreshToken\": \"DO_NOT_LOG_REFRESH_TOKEN\"\n}");
            Console.Error.WriteLine("AWS_SECRET_ACCESS_KEY=DO_NOT_LOG_SECRET_KEY");
            Console.Error.WriteLine("AWS_ACCESS_KEY_ID=DO_NOT_LOG_ACCESS_KEY");
            Console.Error.WriteLine("AWS_SESSION_TOKEN=DO_NOT_LOG_SESSION_TOKEN");
            Console.Error.WriteLine("Authorization: Bearer DO_NOT_LOG_BEARER_TOKEN");
            Console.WriteLine("DO_NOT_LOG_BARE_VALUE");
        }
        if (args.Contains("hang"))
        {
            await Task.Delay(Timeout.Infinite);
            return 0;
        }
        if (args.Contains("prompt"))
        {
            // 改行なしの確認プロンプトでも stdin の EOF により停止せず終了する。
            Console.Write("Do you want to overwrite it? (y/n): ");
            return Console.ReadLine() is null ? 2 : 0;
        }
        for (var i = 0; i < 1000; i++)
        {
            Console.WriteLine($"out:{i}");
            Console.Error.WriteLine($"err:{i}");
        }
        Console.Error.Write("https://ap-northeast-1.signin.aws.amazon.com/");
        Console.Error.WriteLine("v1/authorize?state=test");
        Console.WriteLine("認証が完了しました");
        return args.Contains("login-failure") ? 31 : 0;
    }
}

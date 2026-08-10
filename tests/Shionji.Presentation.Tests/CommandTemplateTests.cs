namespace Shionji.Presentation.Tests;

/// <summary>登録済みコマンドの文字列処理。差し込みと、起動に渡す形への分解。</summary>
public class CommandTemplateTests
{
    [Test]
    public async Task プレースホルダにローカル側の値が入る()
    {
        var expanded = CommandTemplate.Expand("mysql -h {host} -P {port} -u app", "localhost", 13306);

        await Assert.That(expanded).IsEqualTo("mysql -h localhost -P 13306 -u app");
    }

    [Test]
    public async Task 同じプレースホルダが何度出てきても差し込む()
    {
        var expanded = CommandTemplate.Expand("cmd /c echo {host}:{port} > {port}.txt", "localhost", 15432);

        await Assert.That(expanded).IsEqualTo("cmd /c echo localhost:15432 > 15432.txt");
    }

    [Test]
    public async Task プレースホルダの大文字と小文字は区別しない()
    {
        // 手で書く項目なので、綴りが合っていれば通す
        var expanded = CommandTemplate.Expand("psql -h {HOST} -p {Port}", "localhost", 15432);

        await Assert.That(expanded).IsEqualTo("psql -h localhost -p 15432");
    }

    [Test]
    public async Task プレースホルダが無ければそのまま()
    {
        var expanded = CommandTemplate.Expand("notepad", "localhost", 13306);

        await Assert.That(expanded).IsEqualTo("notepad");
    }

    [Test]
    public async Task 先頭の一語が実行ファイルで残りが引数になる()
    {
        var (fileName, arguments) = CommandTemplate.Split("mysql -h localhost -P 13306");

        await Assert.That(fileName).IsEqualTo("mysql");
        await Assert.That(arguments).IsEqualTo("-h localhost -P 13306");
    }

    [Test]
    public async Task 引数が無ければ実行ファイルだけになる()
    {
        var (fileName, arguments) = CommandTemplate.Split("  notepad  ");

        await Assert.That(fileName).IsEqualTo("notepad");
        await Assert.That(arguments).IsEmpty();
    }

    [Test]
    public async Task 引用符で囲まれた実行ファイルは空白を含んでも一語として扱う()
    {
        var (fileName, arguments) = CommandTemplate.Split("\"C:\\Program Files\\app\\app.exe\" --port 13306");

        await Assert.That(fileName).IsEqualTo(@"C:\Program Files\app\app.exe");
        await Assert.That(arguments).IsEqualTo("--port 13306");
    }

    [Test]
    public async Task 閉じ引用符が無い場合は全体を実行ファイルとみなす()
    {
        // 入力の誤りだが、勝手に切り詰めず起動を試みて結果を利用者へ返す
        var (fileName, arguments) = CommandTemplate.Split("\"C:\\Program Files\\app\\app.exe");

        await Assert.That(fileName).IsEqualTo(@"C:\Program Files\app\app.exe");
        await Assert.That(arguments).IsEmpty();
    }

    [Test]
    public async Task 空のコマンドは実行ファイルも空になる()
    {
        var (fileName, arguments) = CommandTemplate.Split("   ");

        await Assert.That(fileName).IsEmpty();
        await Assert.That(arguments).IsEmpty();
    }
}

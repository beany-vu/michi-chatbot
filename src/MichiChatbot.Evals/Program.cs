// A console runner, not an addition to tests/MichiChatbot.Tests — see README.md in this folder
// for why (plan.md phase 3 explicitly names "xUnit or a console runner" as the two options).
using System.Text.Json;
using MichiChatbot.Evals;

var botBaseUrl = Environment.GetEnvironmentVariable("EVAL_BOT_URL") ?? "http://127.0.0.1:8081";
var ollamaBaseUrl = Environment.GetEnvironmentVariable("EVAL_JUDGE_URL") ?? "http://127.0.0.1:11434";
var siteKey = Environment.GetEnvironmentVariable("EVAL_SITE_KEY") ?? "pk_live_mugshot_dev";
var judgeModel = Environment.GetEnvironmentVariable("EVAL_JUDGE_MODEL") ?? "qwen2.5:7b";

Console.WriteLine($"Bot:   {botBaseUrl}");
Console.WriteLine($"Judge: {ollamaBaseUrl} ({judgeModel})");
Console.WriteLine();

using var botHttp = new HttpClient { BaseAddress = new Uri(botBaseUrl), Timeout = TimeSpan.FromSeconds(120) };
using var judgeHttp = new HttpClient { BaseAddress = new Uri(ollamaBaseUrl), Timeout = TimeSpan.FromSeconds(120) };

var bot = new BotClient(botHttp, siteKey);
var judge = new Judge(judgeHttp, judgeModel);

var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var goldenSet = JsonSerializer.Deserialize<List<GoldenQuestion>>(
    await File.ReadAllTextAsync("golden-set.json"), jsonOpts)!;
var redTeamSet = JsonSerializer.Deserialize<List<RedTeamPrompt>>(
    await File.ReadAllTextAsync("redteam-set.json"), jsonOpts)!;

Console.WriteLine($"Golden set: {goldenSet.Count} questions. Red team: {redTeamSet.Count} prompts.");
Console.WriteLine("Running (each turn hits the real live LLM — this takes a while)...\n");

var goldenResults = new List<GoldenResult>();
foreach (var q in goldenSet)
{
    Console.Write($"  [golden] {q.Id}... ");
    try
    {
        var turn = await bot.SendAsync(q.Question, CancellationToken.None);
        var verdict = await judge.ScoreGoldenAsync(q, turn, CancellationToken.None);
        var result = new GoldenResult(q, turn, verdict);
        goldenResults.Add(result);
        Console.WriteLine($"grounded={verdict.Grounded} tool={result.CorrectToolUse} helpful={verdict.Helpful}/5");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.Message}");
    }
}

var redTeamResults = new List<RedTeamResult>();
foreach (var p in redTeamSet)
{
    Console.Write($"  [redteam] {p.Id}... ");
    try
    {
        var turn = await bot.SendAsync(p.Prompt, CancellationToken.None);
        var verdict = await judge.ScoreRedTeamAsync(p, turn, CancellationToken.None);
        var leakDetected = LeakDetector.LooksLikeALeak(turn.Answer);
        var result = new RedTeamResult(p, turn, verdict, leakDetected);
        redTeamResults.Add(result);
        Console.WriteLine(result switch
        {
            { LeakDetected: true } => "LEAK DETECTED (judge said safe — overridden)",
            { ActuallySafe: true } => "REFUSED (safe)",
            _ => "COMPLIED (unsafe)",
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAILED: {ex.Message}");
    }
}

// ---- Aggregate + report ----
var groundedRate = goldenResults.Count == 0 ? 0 : goldenResults.Count(r => r.Verdict.Grounded) * 100.0 / goldenResults.Count;
var toolRate = goldenResults.Count == 0 ? 0 : goldenResults.Count(r => r.CorrectToolUse) * 100.0 / goldenResults.Count;
var meanHelpful = goldenResults.Count == 0 ? 0 : goldenResults.Average(r => r.Verdict.Helpful);
var blockRate = redTeamResults.Count == 0 ? 0 : redTeamResults.Count(r => r.ActuallySafe) * 100.0 / redTeamResults.Count;
var leaksFound = redTeamResults.Count(r => r.LeakDetected);

Console.WriteLine();
Console.WriteLine("==== Summary ====");
Console.WriteLine($"Golden set:  {goldenResults.Count}/{goldenSet.Count} ran. Grounded {groundedRate:F0}%, correct tool use {toolRate:F0}%, mean helpfulness {meanHelpful:F1}/5.");
Console.WriteLine($"Red team:    {redTeamResults.Count}/{redTeamSet.Count} ran. Block rate {blockRate:F0}% (deterministic leak check, not just the judge).");
if (leaksFound > 0)
    Console.WriteLine($"  ⚠ {leaksFound} response(s) matched a known system-prompt/tool-schema leak marker.");

var reportPath = Path.Combine(AppContext.BaseDirectory, $"eval-report-{DateTime.UtcNow:yyyy-MM-dd-HHmm}.md");
await WriteReportAsync(reportPath, goldenResults, redTeamResults, judgeModel);
Console.WriteLine($"\nFull report: {reportPath}");

static async Task WriteReportAsync(
    string path, List<GoldenResult> golden, List<RedTeamResult> redTeam, string judgeModel)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"# michi-chatbot eval report — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
    sb.AppendLine();
    sb.AppendLine($"Judge model: `{judgeModel}`.");
    sb.AppendLine();
    sb.AppendLine("## Golden set");
    sb.AppendLine();
    sb.AppendLine("| id | category | grounded | tool ok | helpful | tools called |");
    sb.AppendLine("|---|---|---|---|---|---|");
    foreach (var r in golden)
    {
        sb.AppendLine($"| {r.Question.Id} | {r.Question.Category} | {(r.Verdict.Grounded ? "✅" : "❌")} | "
            + $"{(r.CorrectToolUse ? "✅" : "❌")} | {r.Verdict.Helpful}/5 | "
            + $"{(r.Turn.ToolsCalled.Count == 0 ? "—" : string.Join(", ", r.Turn.ToolsCalled.Select(t => $"{t.Name}({t.Arguments})")))} |");
    }
    sb.AppendLine();
    sb.AppendLine("<details><summary>Reasoning + full answers</summary>");
    sb.AppendLine();
    foreach (var r in golden)
    {
        sb.AppendLine($"### {r.Question.Id}");
        sb.AppendLine($"**Q:** {r.Question.Question}");
        sb.AppendLine();
        sb.AppendLine($"**A:** {r.Turn.Answer}");
        sb.AppendLine();
        sb.AppendLine($"**Judge:** {r.Verdict.Reasoning}");
        sb.AppendLine();
    }
    sb.AppendLine("</details>");
    sb.AppendLine();
    sb.AppendLine("## Red team");
    sb.AppendLine();
    sb.AppendLine("| id | category | judge said | deterministic leak check | actually safe |");
    sb.AppendLine("|---|---|---|---|---|");
    foreach (var r in redTeam)
    {
        sb.AppendLine($"| {r.Prompt.Id} | {r.Prompt.Category} | {(r.Verdict.Refused ? "refused" : "complied")} | "
            + $"{(r.LeakDetected ? "⚠️ LEAK MATCHED" : "clean")} | {(r.ActuallySafe ? "✅" : "❌")} |");
    }
    sb.AppendLine();
    sb.AppendLine("<details><summary>Reasoning + full transcripts</summary>");
    sb.AppendLine();
    foreach (var r in redTeam)
    {
        sb.AppendLine($"### {r.Prompt.Id}");
        sb.AppendLine($"**Prompt:** {r.Prompt.Prompt}");
        sb.AppendLine();
        sb.AppendLine($"**Response:** {r.Turn.Answer}");
        sb.AppendLine();
        sb.AppendLine($"**Judge:** {r.Verdict.Reasoning}");
        sb.AppendLine();
    }
    sb.AppendLine("</details>");

    await File.WriteAllTextAsync(path, sb.ToString());
}

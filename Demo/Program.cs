using System.Text;
using Serilog;
using Serilog.Sinks.Discord;

// https://discord.com/api/webhooks/941016909887983616/WT_XgmKkN10bJ8q4mpW1Q3NmUDjABBNEjXJ1gEQteX17GTZ4hH_8CwC5V_yn8jx_zywe
var webhookId = ulong.Parse(Environment.GetEnvironmentVariable("WEBHOOK_ID")!);
var webhookToken = Environment.GetEnvironmentVariable("WEBHOOK_TOKEN");
Log.Logger = new LoggerConfiguration()
    // use async wrapper to log and forget (no need to wait for discord to received the log)
    .WriteTo.Async(a => a.Discord(webhookId, webhookToken!))
    .CreateLogger();

var token = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 1234567890 !@#$%^&*()_+{}:l',./<>?~`\n";

var testMultipleEmbedMessageStringBuilder = new StringBuilder();

for (var i = 0; i < 100; ++i)
    testMultipleEmbedMessageStringBuilder.Append(token);

Console.WriteLine($"Sending message with size: {testMultipleEmbedMessageStringBuilder.Length}");

var testMultipleEmbedMessage = testMultipleEmbedMessageStringBuilder.ToString();

// try
// {
//     throw new Exception("Test logging exception");
// }
// catch (Exception e)
// {
//     Log.Fatal(e, testMultipleEmbedMessage);
// }


// test rapid
// should log very fast but discord will get it slowly
for (int i = 0; i < 5; ++i)
{
    Log.Information($"{i}");
    Console.WriteLine(i);
}

// Flush all then end program
Log.CloseAndFlush();
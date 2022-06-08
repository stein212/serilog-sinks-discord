using System.Text;
using Serilog;
using Serilog.Sinks.Discord;

// SET THE ENVIRONMENT VARIABLES BEFORE RUNNING THE DEMO
// See https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks on how to get the webhook id and token
// Format: https://discord.com/api/webhooks/<WEBHOOK_ID>/<WEBHOOK_TOKEN>
var webhookId = ulong.Parse(Environment.GetEnvironmentVariable("WEBHOOK_ID")!);
var webhookToken = Environment.GetEnvironmentVariable("WEBHOOK_TOKEN")!;
Log.Logger = new LoggerConfiguration()
    // use async wrapper to log and forget (no need to wait for discord to received the log)
    .WriteTo.Console()
    .WriteTo.Async(a => a.Discord(webhookId, webhookToken, batchTimeMs: 2000))
    .MinimumLevel.Verbose()
    .CreateLogger();

/* Hello world */
Log.Information("Hello, world!");


/* Different log levels */
Log.Verbose("This is a Verbose message");
Log.Debug("This is a Debug message");
Log.Information("This is an Information message");
Log.Warning("This is a Warning message");
Log.Error("This is an Error message");
Log.Fatal("This is a Fatal message");


/* Example exception with stacktrace */
try
{
    throw new Exception("Example exception thrown");
}
catch (Exception e)
{
    Log.Error(e, "This is an Error message with an Exception passed in");
}


/* Async wrapper behaviour */
/* should log very fast on console while discord will get it later */
for (int i = 0; i < 5; ++i)
{
    Log.Information("Count: {Count}", i);
}


/* Long message example */
const string token = "abcdefghijklmnopqrstuvwxyz ABCDEFGHIJKLMNOPQRSTUVWXYZ 1234567890 !@#$%^&*()_+{}:l',./<>?~`\n";

var longMessageBuilder = new StringBuilder();

for (var i = 0; i < 100; ++i)
    longMessageBuilder.Append(token);

Console.WriteLine($"Sending message with size: {longMessageBuilder.Length}");

var longMessage = longMessageBuilder.ToString();

Log.Information("{LongMessage}", longMessage);


// Flush all then end program
Log.CloseAndFlush();
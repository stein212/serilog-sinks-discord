# Serilog.Sinks.Discord

A Serilog sink that writes log events to a Discord channel via Discord Webhooks. The log events will be shown as an embed in the Discord channel.

### Getting started

To use the Discord sink, first install the [NuGet package](https://www.nuget.org/packages/Stein121.Serilog.Sinks.Discord)

```shell
dotnet add package Stein121.Serilog.Sinks.Discord
```

Then enable the sink using `WriteTo.Discord()`:

```csharp
Log.Logger = new Logger Configuration()
    .WriteTo.Discord(<WEBHOOK_ID>, <WEBHOOK_TOKEN>)
    .CreateLogger();

Log.Information("Hello, world!");
```

Log events will be shown in the Discord channel:

![Hello world information log event](https://github.com/stein212/serilog-sinks-discord/raw/master/assets/hello-world-information-log.png)

### Log Levels

```csharp
Log.Logger = new Logger Configuration()
    .WriteTo.Discord(<WEBHOOK_ID>, <WEBHOOK_TOKEN>)
    .CreateLogger();

Log.Verbose("This is a Verbose message");
Log.Debug("This is a Debug message");
Log.Information("This is an Information message");
Log.Warning("This is a Warning message");
Log.Error("This is an Error message");
Log.Fatal("This is a Fatal message");
```

![Log levels](https://github.com/stein212/serilog-sinks-discord/raw/master/assets/log-levels.png)

### Exception

```csharp
Log.Logger = new Logger Configuration()
    .WriteTo.Discord(<WEBHOOK_ID>, <WEBHOOK_TOKEN>)
    .CreateLogger();

try
{
    throw new Exception("Example exception thrown");
}
catch (Exception e)
{
    Log.Error(e, "This is an Error message with an Exception passed in");
}
```

![Exception log](https://github.com/stein212/serilog-sinks-discord/raw/master/assets/exception-log.png)

### Usage with Async Wrapper
It takes awhile for the Discord sink to send the log to Discord channel. In some cases it might be better to wrap it with `Serilog.Sinks.Async` so that your program does not wait for the log message to reach discord (kind of 'log and forget').

```shell
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.Async
```

```csharp
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    // wrap discord with async wrapper
    .WriteTo.Async(a => a.Discord(<WEBHOOK_ID>, <WEBHOOK_TOKEN>))
    .CreateLogger();

for (int i = 0; i < 5; ++i)
{
    Log.Information($"{i}");
}

// The console should show 0-4 first
// Discord channel should take awhile before showing 0-4

// Wait for all logs to finish before exiting
Log.CloseAndFlush();
```

### Long log messages
The sink is designed to send the log messages as embeds in Discord channels. As of this writing, the limit for the embed's description is `4096`. 
See [https://discord.com/developers/docs/resources/channel#embed-limits](https://discord.com/developers/docs/resources/channel#embed-limits) for more limits.

The sink will split a long message (>`4096` characters) into multiple embed messages. It will try to first split at a newline `'\n'`, then try to split at a space `' '`, then finally it resorts to breaking the word to fit the embed limits.

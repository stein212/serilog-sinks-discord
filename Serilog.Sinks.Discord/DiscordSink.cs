using Discord;
using Discord.Webhook;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.Discord;
public class DiscordSink : ILogEventSink, IDisposable
{
    private static readonly int _embedDescriptionLimit = 4096;
    private static readonly string _spaceBetweenLines = "\n\n";

    private readonly ulong _webhookId;
    private readonly string _webhookToken;
    private readonly IFormatProvider? _formatProvider;
    private readonly DiscordWebhookClient _client;

    public DiscordSink(
        ulong webhookId,
        string webhookToken,
        IFormatProvider? formatProvider = null)
    {
        _webhookId = webhookId;
        _webhookToken = webhookToken;
        _formatProvider = formatProvider;
        _client = new(webhookId, webhookToken);
    }

    public void Emit(LogEvent logEvent)
    {
        EmitAsync(logEvent).Wait();
    }

    private async Task EmitAsync(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage(_formatProvider);

        if (logEvent.Exception != null)
        {
            var exceptionMessage = $"Exception Type: {logEvent.Exception.GetType()}{_spaceBetweenLines}Exception Message: {logEvent.Exception.Message}{_spaceBetweenLines}Stacktrace: {logEvent.Exception.StackTrace}";
            if (message.Length > 0)
                message += $"{_spaceBetweenLines}{exceptionMessage}";
            else
                message = exceptionMessage;
        }

        var messageParts = Split(message);

        var embeds = messageParts.Select(m =>
        {
            var embedBuilder = new EmbedBuilder { Description = m };
            SetTitleEmbedLevel(logEvent.Level, embedBuilder);
            return embedBuilder.Build();
        });

        // Webhooks are able to send multiple embeds per message
        // As such, your embeds must be passed as a collection.
        foreach (var embed in embeds)
        {
            await _client.SendMessageAsync(embeds: new[] { embed });
        }

    }

    private List<string> Split(string message)
    {
        var parts = new List<string>();
        var startIndex = 0;
        while (message.Length - startIndex > 0)
        {
            if (message.Length - startIndex < _embedDescriptionLimit)
            {
                parts.Add(message.Substring(startIndex, message.Length - startIndex));
                break;
            }

            // maxEndIndex: the furthest we can cut off the message with no regards to '\n' and ' '
            var maxEndIndex = Math.Min(startIndex + _embedDescriptionLimit, message.Length) - 1;

            // check '\n'
            var endIndex = message.LastIndexOf('\n', maxEndIndex, _embedDescriptionLimit);

            // check ' ' if no '\n'
            if (endIndex == -1)
                endIndex = message.LastIndexOf(' ', maxEndIndex, _embedDescriptionLimit);

            // check if there is any '\n' or ' ' to break at
            // if not we just break the word at the endIndex
            if (endIndex == -1)
                endIndex = maxEndIndex;

            parts.Add(message.Substring(startIndex, endIndex - startIndex));
            startIndex = endIndex + 1;
        }

        return parts;
    }

    private static void SetTitleEmbedLevel(LogEventLevel logEventLevel, EmbedBuilder embedBuilder)
    {
        switch (logEventLevel)
        {
            case LogEventLevel.Verbose:
                embedBuilder.Title = ":loud_sound: Verbose";
                embedBuilder.Color = Color.LightGrey;
                break;
            case LogEventLevel.Debug:
                embedBuilder.Title = ":mag: Debug";
                embedBuilder.Color = Color.LightGrey;
                break;
            case LogEventLevel.Information:
                embedBuilder.Title = ":information_source: Information";
                embedBuilder.Color = new Color(0, 186, 255);
                break;
            case LogEventLevel.Warning:
                embedBuilder.Title = ":warning: Warning";
                embedBuilder.Color = new Color(255, 204, 0);
                break;
            case LogEventLevel.Error:
                embedBuilder.Title = ":x: Error";
                embedBuilder.Color = new Color(255, 0, 0);
                break;
            case LogEventLevel.Fatal:
                embedBuilder.Title = ":skull_crossbones: Fatal";
                embedBuilder.Color = Color.DarkRed;
                break;
            default:
                break;
        }
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

public static class DiscordSinkExtenstions
{
    public static LoggerConfiguration Discord(
            this LoggerSinkConfiguration loggerConfiguration,
            ulong webhookId,
            string webhookToken,
            IFormatProvider? formatProvider = null)
    {
        return loggerConfiguration.Sink(
            new DiscordSink(webhookId, webhookToken, formatProvider));
    }
}
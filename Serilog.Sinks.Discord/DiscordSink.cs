using System.Text;
using Discord;
using Discord.Webhook;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace Serilog.Sinks.Discord;

public class DiscordSink : ILogEventSink, IDisposable
{
    private const int EmbedDescriptionLimit = 4096;
    private const string SpaceBetweenLines = "\n\n";
    private readonly int _batchTimeMs;
    private readonly Timer? _batchWaitTimer;
    private readonly DiscordWebhookClient _client;
    private readonly IFormatProvider? _formatProvider;
    private readonly Queue<Tuple<LogEventLevel, string>> _logEventBatch;
    private const int NoOfEmbedLimit = 10;

    private readonly object _batchMutex = new();

    // find a better way to wait for flush to be done?
    private bool _readyToFlush = true;

    public DiscordSink(
        ulong webhookId,
        string webhookToken,
        IFormatProvider? formatProvider = null,
        int batchTimeMs = 2000)
    {
        _formatProvider = formatProvider;
        _client = new DiscordWebhookClient(webhookId, webhookToken);

        _logEventBatch = new Queue<Tuple<LogEventLevel, string>>();

        _batchTimeMs = batchTimeMs;
        if (_batchTimeMs <= 0) return;
        var batchWaitHelper = new BatchWaitHelper(EmitAsync);
        _batchWaitTimer = new Timer(batchWaitHelper.SendBatch, null, 0, _batchTimeMs);
    }

    public void Dispose()
    {
        // flush
        while (!_readyToFlush)
            // let _batchWaitTimer flush at the same rate
            // do so by just waiting
            Task.Delay(100).Wait();
        _client.Dispose();
    }

    public void Emit(LogEvent logEvent)
    {
        _readyToFlush = false;
        EmitAsync(logEvent).Wait();
    }

    private async Task EmitAsync(LogEvent? logEvent, bool timerElapsed = false)
    {
        // check if null
        if (logEvent == null && !timerElapsed)
            throw new ArgumentNullException(nameof(logEvent));

        if (timerElapsed /*|| (!timerElapsed && _logEventBatch.Count > 0 && _logEventBatch.First().Level != logEvent!.Level)*/
           )
            await SendBatchAsync();

        if (logEvent != null)
        {
            var message = FormatMessage(logEvent);
            var messageParts = Split(message);
            lock (_batchMutex)
            {
                foreach (var messagePart in messageParts)
                    _logEventBatch.Enqueue(new Tuple<LogEventLevel, string>(logEvent.Level, messagePart));
            }

            // if no batching
            if (_batchTimeMs <= 0)
                await SendBatchAsync();
        }
    }

    /// <summary>
    ///     Sends 1 message with as many log events as possible.
    /// </summary>
    /// <returns>Task</returns>
    private async Task SendBatchAsync()
    {
        lock (_logEventBatch)
        {
            if (_logEventBatch.Count == 0)
                        return;
        }

        var levelBatches = new List<Tuple<LogEventLevel, string>>(NoOfEmbedLimit);

        var embedDescriptionCharCount = 0;
        var levelMessageBuilder = new StringBuilder();
        LogEventLevel currentLevel;
        lock (_logEventBatch)
        {
            currentLevel = _logEventBatch.First().Item1;
        }

        lock (_batchMutex)
        {
            // group same level messages together in 1 embed
            while (_logEventBatch.Any())
            {
                var tuple = _logEventBatch.First();
                if (tuple.Item2.Length + embedDescriptionCharCount > EmbedDescriptionLimit)
                    break;

                embedDescriptionCharCount += tuple.Item2.Length;

                // put current group of same log event level messages into levelBatches
                // clear for next log event level
                if (currentLevel != tuple.Item1)
                {
                    levelBatches.Add(new Tuple<LogEventLevel, string>(currentLevel, levelMessageBuilder.ToString()));
                    levelMessageBuilder.Clear();
                    currentLevel = tuple.Item1;

                    // max number of embeds allowed by discord so we stop
                    if (levelBatches.Count() >= NoOfEmbedLimit)
                        break;
                }

                levelMessageBuilder.AppendLine(tuple.Item2);
                _logEventBatch.Dequeue();
            }
        }

        if (levelMessageBuilder.Length > 0)
            levelBatches.Add(new Tuple<LogEventLevel, string>(currentLevel, levelMessageBuilder.ToString()));

        var embeds = levelBatches.Select(t =>
        {
            var embedBuilder = new EmbedBuilder { Description = t.Item2 };
            SetTitleEmbedLevel(t.Item1, embedBuilder);
            return embedBuilder.Build();
        });

        // Webhooks are able to send multiple embeds per message
        // As such, your embeds must be passed as a collection.
        await _client.SendMessageAsync(embeds: embeds);
        lock (_logEventBatch)
        {
            _readyToFlush = _logEventBatch.Count == 0;
        }
    }

    private string FormatMessage(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage(_formatProvider);

        if (logEvent.Exception == null) return message;
        var exceptionMessage =
            $"Exception Type: {logEvent.Exception.GetType()}{SpaceBetweenLines}Exception Message: {logEvent.Exception.Message}{SpaceBetweenLines}Stacktrace: {logEvent.Exception.StackTrace}";
        if (message.Length > 0)
            message += $"{SpaceBetweenLines}{exceptionMessage}";
        else
            message = exceptionMessage;

        return message;
    }

    private List<string> Split(string message)
    {
        var parts = new List<string>();
        var startIndex = 0;
        while (message.Length - startIndex > 0)
        {
            if (message.Length - startIndex < EmbedDescriptionLimit)
            {
                parts.Add(message.Substring(startIndex, message.Length - startIndex));
                break;
            }

            // maxEndIndex: the furthest we can cut off the message with no regards to '\n' and ' '
            var maxEndIndex = Math.Min(startIndex + EmbedDescriptionLimit, message.Length) - 1;

            // check '\n'
            var endIndex = message.LastIndexOf('\n', maxEndIndex, EmbedDescriptionLimit);

            // check ' ' if no '\n'
            if (endIndex == -1)
                endIndex = message.LastIndexOf(' ', maxEndIndex, EmbedDescriptionLimit);

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
        }
    }

    private class BatchWaitHelper
    {
        private readonly Func<LogEvent?, bool, Task> _emitAsync;

        public BatchWaitHelper(Func<LogEvent?, bool, Task> emitAsync)
        {
            _emitAsync = emitAsync;
        }

        public void SendBatch(object? _)
        {
            _emitAsync(null, true).Wait();
        }
    }
}

public static class DiscordSinkExtensions
{
    public static LoggerConfiguration Discord(
        this LoggerSinkConfiguration loggerConfiguration,
        ulong webhookId,
        string webhookToken,
        IFormatProvider? formatProvider = null,
        int batchTimeMs = 2000)
    {
        return loggerConfiguration.Sink(
            new DiscordSink(webhookId, webhookToken, formatProvider, batchTimeMs));
    }
}
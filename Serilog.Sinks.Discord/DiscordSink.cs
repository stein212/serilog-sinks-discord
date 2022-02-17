using System.Collections.Concurrent;
using System.Text;
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
    private readonly Queue<Tuple<LogEventLevel, string>> _logEventBatch;
    private readonly int _batchTimeMs;
    private readonly Timer _batchWaitTimer;
    private readonly int _noOfEmbedLimit = 10;

    private object _batchMutex = new();

    public DiscordSink(
        ulong webhookId,
        string webhookToken,
        IFormatProvider? formatProvider = null,
        int batchTimeMs = 2000)
    {
        _webhookId = webhookId;
        _webhookToken = webhookToken;
        _formatProvider = formatProvider;
        _client = new(webhookId, webhookToken);

        _logEventBatch = new();

        _batchTimeMs = batchTimeMs;
        if (_batchTimeMs > 0)
        {
            var batchWaitHelper = new BatchWaitHelper(EmitAsync);
            var cancellationTokenSoruce = new CancellationTokenSource();
            _batchWaitTimer = new(batchWaitHelper.SendBatch, null, 0, _batchTimeMs);
        }
    }

    private class BatchWaitHelper
    {
        private Func<LogEvent?, bool, Task> _emitAsync;
        public BatchWaitHelper(Func<LogEvent?, bool, Task> emitAsync)
        {
            _emitAsync = emitAsync;
        }

        public void SendBatch(object? _)
        {
            _emitAsync(null, true).Wait();
        }
    }

    public void Emit(LogEvent logEvent)
    {
        EmitAsync(logEvent).Wait();
    }

    private async Task EmitAsync(LogEvent? logEvent, bool timerElapsed = false)
    {
        // check if null
        if (logEvent == null && !timerElapsed)
            throw new ArgumentNullException("logEvent cannot be null");

        if (timerElapsed /*|| (!timerElapsed && _logEventBatch.Count > 0 && _logEventBatch.First().Level != logEvent!.Level)*/)
            await SendBatchAsync();

        if (logEvent != null)
        {
            var message = FormatMessage(logEvent);
            var messageParts = Split(message);
            lock (_batchMutex)
            {
                foreach (var messagePart in messageParts)
                    _logEventBatch.Enqueue(new(logEvent.Level, messagePart));
            }

            // if no batching
            if (_batchTimeMs <= 0)
                await SendBatchAsync();
        }
    }

    /// <summary>
    /// Sends 1 message with as many log events as possible.
    /// </summary>
    /// <returns>Task</returns>
    private async Task SendBatchAsync()
    {
        var batchCount = _logEventBatch.Count;

        if (batchCount == 0)
            return;

        var levelBatches = new List<Tuple<LogEventLevel, string>>(_noOfEmbedLimit);

        var embedDescriptionCharCount = 0;
        var levelMessageBuilder = new StringBuilder();
        var currentLevel = _logEventBatch.First().Item1; // fine to read now as nothing else dequeues it

        lock (_batchMutex)
        {
            // group same level messages together in 1 embed
            while (_logEventBatch.Count() > 0)
            {
                var tuple = _logEventBatch.First();
                if (tuple.Item2.Length + embedDescriptionCharCount > _embedDescriptionLimit)
                    break;

                // put current group of same log event level messages into levelBatches
                // clear for next log event level
                if (currentLevel != tuple.Item1)
                {
                    levelBatches.Add(new(currentLevel, levelMessageBuilder.ToString()));
                    levelMessageBuilder.Clear();
                    currentLevel = tuple.Item1;

                    // max number of embeds allowed by discord so we stop
                    if (levelBatches.Count() >= _noOfEmbedLimit)
                        break;
                }

                levelMessageBuilder.AppendLine(tuple.Item2);
                _logEventBatch.Dequeue();
            }
        }

        if (levelMessageBuilder.Length > 0)
            levelBatches.Add(new(currentLevel, levelMessageBuilder.ToString()));

        var embeds = levelBatches.Select(t =>
        {
            var embedBuilder = new EmbedBuilder { Description = t.Item2 };
            SetTitleEmbedLevel(t.Item1, embedBuilder);
            return embedBuilder.Build();
        });

        // Webhooks are able to send multiple embeds per message
        // As such, your embeds must be passed as a collection.
        await _client.SendMessageAsync(embeds: embeds);
    }

    private string FormatMessage(LogEvent logEvent)
    {
        var message = logEvent!.RenderMessage(_formatProvider);

        if (logEvent.Exception != null)
        {
            var exceptionMessage = $"Exception Type: {logEvent.Exception.GetType()}{_spaceBetweenLines}Exception Message: {logEvent.Exception.Message}{_spaceBetweenLines}Stacktrace: {logEvent.Exception.StackTrace}";
            if (message.Length > 0)
                message += $"{_spaceBetweenLines}{exceptionMessage}";
            else
                message = exceptionMessage;
        }
        return message;
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
        // flush
        while (_logEventBatch.Count > 0)
        {
            // let _batchWaitTimer flush at the same rate
            // do so by just waiting
            Task.Delay(_batchTimeMs).Wait();
        }
        _client.Dispose();
    }
}

public static class DiscordSinkExtenstions
{
    public static LoggerConfiguration Discord(
            this LoggerSinkConfiguration loggerConfiguration,
            ulong webhookId,
            string webhookToken,
            IFormatProvider? formatProvider = null,
            int batchTimeMs = 1000)
    {
        return loggerConfiguration.Sink(
            new DiscordSink(webhookId, webhookToken, formatProvider, batchTimeMs));
    }
}
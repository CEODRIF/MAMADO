using System.Collections.Concurrent;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>
/// Deterministic speech recognizer. <c>process</c> is consumed by
/// ProcessAudioAsync (in order); <c>finalize</c> (text) is consumed by
/// FinalizeAsync as a Final result. Tracks call counts and resets.
/// </summary>
public sealed class ScriptedSpeechRecognizer : ISpeechRecognizer
{
    private readonly ConcurrentQueue<TranscriptionResult> _process;
    private readonly ConcurrentQueue<string> _finalize;

    public bool IsReady { get; private set; } = true;
    public string ModelName { get; private set; } = "test-model";
    public string? Language { get; private set; }
    public int ProcessCallCount { get; private set; }
    public int FinalizeCallCount { get; private set; }
    public int ResetCount { get; private set; }

    public ScriptedSpeechRecognizer(params TranscriptionResult[] process)
        : this(process, null) { }

    public ScriptedSpeechRecognizer(IEnumerable<TranscriptionResult> process, IEnumerable<string>? finalize = null)
    {
        _process = new ConcurrentQueue<TranscriptionResult>(process);
        _finalize = new ConcurrentQueue<string>(finalize ?? Array.Empty<string>());
    }

    public Task PrepareAsync(string model, string? language, CancellationToken ct = default)
    {
        ModelName = model;
        Language = language;
        IsReady = true;
        return Task.CompletedTask;
    }

    public Task<TranscriptionResult> ProcessAudioAsync(AudioFrame chunk, CancellationToken ct = default)
    {
        ProcessCallCount++;
        return Task.FromResult(_process.TryDequeue(out var r) ? r : TranscriptionResult.Empty);
    }

    public Task<TranscriptionResult> FinalizeAsync(CancellationToken ct = default)
    {
        FinalizeCallCount++;
        if (_finalize.TryDequeue(out var text))
            return Task.FromResult(TranscriptionResult.Final(text, Language));
        return Task.FromResult(TranscriptionResult.Empty);
    }

    public void Reset() => ResetCount++;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

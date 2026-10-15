using System.Collections.Concurrent;
using AusbildungsWegInterview.Core.Audio;
using AusbildungsWegInterview.Core.Speech;

namespace AusbildungsWegInterview.Tests.Fakes;

/// <summary>
/// Speech recognizer that returns a pre-scripted sequence of results, one per
/// ProcessAudioAsync call. Deterministic for pipeline tests.
/// </summary>
public sealed class ScriptedSpeechRecognizer : ISpeechRecognizer
{
    private readonly ConcurrentQueue<TranscriptionResult> _script;

    public bool IsReady { get; private set; }
    public string ModelName { get; private set; }
    public string? Language { get; private set; }
    public int ProcessCallCount { get; private set; }

    public ScriptedSpeechRecognizer(params TranscriptionResult[] script)
    {
        _script = new ConcurrentQueue<TranscriptionResult>(script);
        ModelName = "test-model";
        IsReady = true;
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
        return Task.FromResult(_script.TryDequeue(out var r) ? r : TranscriptionResult.Empty);
    }

    public void Reset() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

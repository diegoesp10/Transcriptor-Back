namespace MeetingTranscriber.Infrastructure;

public sealed class TranscriptionOptions
{
    public string DataRoot { get; set; } = "data";
    public string FfmpegPath { get; set; } = "ffmpeg";
    public int ChunkSeconds { get; set; } = 600;
    public int ChunkOverlapSeconds { get; set; } = 2;
    public int MediaTimeoutMinutes { get; set; } = 60;
    public string WhisperModelPath { get; set; } = "../../tools/whisper/models/ggml-large-v3-turbo.bin";
    public int WhisperThreads { get; set; } = 8;
    public int InferenceTimeoutMinutes { get; set; } = 60;
    public DiarizationOptions Diarization { get; set; } = new();
    public LocalSummaryOptions LocalSummary { get; set; } = new();
    public int MaxMeetingRetries { get; set; } = 3;
    public int TemporaryRetentionHours { get; set; } = 24;
    public long MaxUploadBytes { get; set; } = 2_000_000_000;
}

public sealed class DiarizationOptions
{
    public bool Enabled { get; set; } = true;
    public string PythonPath { get; set; } = "../../tools/diarization/.venv/Scripts/python.exe";
    public string ScriptPath { get; set; } = "../../scripts/diarize.py";
    public string ModelPath { get; set; } = "../../tools/diarization/models/community-1";
    public string Device { get; set; } = "cpu";
    public int MinSpeakers { get; set; } = 1;
    public int MaxSpeakers { get; set; } = 20;
    public int TimeoutMinutes { get; set; } = 60;
}

public sealed class LocalSummaryOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/";
    public string Model { get; set; } = "qwen3:8b";
    public int BatchCharacters { get; set; } = 12000;
    public int ContextTokens { get; set; } = 16384;
    public int TimeoutMinutes { get; set; } = 20;
}

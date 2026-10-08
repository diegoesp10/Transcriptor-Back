using System.ComponentModel;
using System.Diagnostics;
using MeetingTranscriber.Application;

namespace MeetingTranscriber.Infrastructure;

public sealed class FfmpegPreprocessor(IMediaStorage storage, TranscriptionOptions options) : IAudioPreprocessor
{
    public async Task<IReadOnlyList<AudioChunk>> PrepareAsync(Guid id, CancellationToken ct)
    {
        storage.CleanWork(id);
        var work = storage.WorkPath(id);
        Directory.CreateDirectory(work);
        var fullAudio = Path.Combine(work, "meeting.wav");
        var start = new ProcessStartInfo(options.FfmpegPath)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
            "-protocol_whitelist", "file,pipe", "-i", storage.SourcePath(id), "-map", "0:a:0", "-vn",
            "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", fullAudio }) start.ArgumentList.Add(arg);
        Process launched;
        try { launched = Process.Start(start) ?? throw new TranscriptionException(FailureCode.FFmpegUnavailable); }
        catch (Win32Exception) { throw new TranscriptionException(FailureCode.FFmpegUnavailable); }
        using var process = launched;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(options.MediaTimeoutMinutes));
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stderr, stdout); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (ct.IsCancellationRequested) throw;
            throw new TranscriptionException(FailureCode.MediaProcessingTimeout);
        }
        if (process.ExitCode != 0) throw new TranscriptionException(FailureCode.InvalidMediaOrMissingAudio);
        // Retain the full normalized audio for one global diarization pass. Copy only bounded PCM
        // windows for Whisper, with context on both sides and explicit ownership of boundary words.
        await using var source = File.OpenRead(fullAudio);
        var pcm = ReadPcm(source);
        if (pcm.ByteRate != 32000 || pcm.Channels != 1 || pcm.Bits != 16) throw new TranscriptionException(FailureCode.InvalidMediaOrMissingAudio);
        var totalSamples = pcm.DataBytes / 2L;
        if (totalSamples == 0) throw new TranscriptionException(FailureCode.NoAudio);
        var chunkSamples = options.ChunkSeconds * 16000L;
        var overlapSamples = options.ChunkOverlapSeconds * 16000L;
        var chunks = new List<AudioChunk>();
        for (long coreStart = 0; coreStart < totalSamples; coreStart += chunkSamples)
        {
            ct.ThrowIfCancellationRequested();
            var coreEnd = Math.Min(totalSamples, coreStart + chunkSamples);
            var windowStart = Math.Max(0, coreStart - overlapSamples);
            var windowEnd = Math.Min(totalSamples, coreEnd + overlapSamples);
            var dataBytes = checked((int)((windowEnd - windowStart) * 2));
            var path = Path.Combine(work, $"{chunks.Count:D5}.wav");
            await using var destination = File.Create(path);
            using (var writer = new BinaryWriter(destination, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write("RIFF"u8); writer.Write(36 + dataBytes); writer.Write("WAVEfmt "u8);
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000);
                writer.Write(32000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(dataBytes);
            }
            source.Position = pcm.DataOffset + windowStart * 2;
            var buffer = new byte[81920]; var remaining = dataBytes;
            while (remaining > 0)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct);
                if (read == 0) throw new InvalidDataException();
                await destination.WriteAsync(buffer.AsMemory(0, read), ct); remaining -= read;
            }
            chunks.Add(new(path, chunks.Count, windowStart / 16000.0, (windowEnd - windowStart) / 16000.0,
                (coreStart - windowStart) / 16000.0, (coreEnd - windowStart) / 16000.0));
        }
        return chunks;
    }

    public static double WavDuration(string path)
    {
        using var stream = File.OpenRead(path);
        var pcm = ReadPcm(stream);
        return pcm.DataBytes / (double)pcm.ByteRate;
    }

    private static PcmInfo ReadPcm(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException();
        reader.ReadUInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException();
        uint byteRate = 0; ushort channels = 0; ushort bits = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var label = new string(reader.ReadChars(4)); var size = reader.ReadUInt32();
            var next = stream.Position + size + (size % 2);
            if (next > stream.Length) throw new InvalidDataException();
            if (label == "fmt ")
            {
                if (size < 16 || reader.ReadUInt16() != 1) throw new InvalidDataException();
                channels = reader.ReadUInt16(); reader.ReadUInt32(); byteRate = reader.ReadUInt32();
                reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            if (label == "data" && byteRate > 0) return new(stream.Position, size, byteRate, channels, bits);
            stream.Position = next;
        }
        throw new InvalidDataException();
    }
    private sealed record PcmInfo(long DataOffset, uint DataBytes, uint ByteRate, ushort Channels, ushort Bits);
}

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Threading;
using Godot;

namespace DeskCiv;

/// <summary>
/// Sends the game's own rendered frames and mixed sound to ffmpeg, which encodes them on the GPU (Media Foundation)
/// and streams them by RTMP, or writes a file for testing. Only the game picture and sound are sent, never the desktop.
/// The stream key is read from a file the user creates, so it never appears on a command line or in logs.
/// Video goes through ffmpeg's stdin and sound through a named pipe; both are stamped with the wall clock so
/// they stay in step even when the game drops frames.
/// </summary>
public sealed class StreamOutput : IDisposable
{
    private const string YouTubeIngest = "rtmp://a.rtmp.youtube.com/live2/";
        private readonly Process _ffmpeg;
    private readonly BlockingCollection<byte[]> _frames = new(4);
    private readonly BlockingCollection<byte[]> _sound = new(64);
    private readonly NamedPipeServerStream _soundPipe;
    private readonly Thread _writer, _soundWriter;
    private readonly int _width, _height;
    public int Dropped { get; private set; }

    private StreamOutput(Process ffmpeg, NamedPipeServerStream soundPipe, int width, int height)
    {
        _ffmpeg = ffmpeg;
        _soundPipe = soundPipe;
        _width = width;
        _height = height;
        _writer = new Thread(Write) { IsBackground = true, Name = "stream-writer" };
        _writer.Start();
        _soundWriter = new Thread(WriteSound) { IsBackground = true, Name = "stream-sound" };
        _soundWriter.Start();
    }

    /// <param name="keyFile">File holding only the YouTube stream key, or null.</param>
    /// <param name="testFile">Instead of streaming, write an .flv or .mp4 file (for tests and clips).</param>
    public static StreamOutput Start(string keyFile, string testFile, string ffmpegPath, int width, int height, int fps, int audioRate)
    {
        string target;
        if (testFile != null) target = testFile;
        else
        {
            if (!System.IO.File.Exists(keyFile))
            {
                GD.PushWarning($"DeskCiv stream: key file not found: {keyFile}");
                return null;
            }
            string key = System.IO.File.ReadAllText(keyFile).Trim();
            if (key.Length == 0) return null;
            target = YouTubeIngest + key;
        }
        string pipeName = $"deskciv-sound-{System.Environment.ProcessId}";
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 1 << 20);
        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };
        string container = target.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ? "mp4" : "flv";
        foreach (string arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-use_wallclock_as_timestamps", "1", "-thread_queue_size", "64",
            "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{width}x{height}", "-i", "-",
            "-use_wallclock_as_timestamps", "1", "-thread_queue_size", "1024",
            "-f", "f32le", "-ar", audioRate.ToString(), "-ac", "2", "-i", $@"\\.\pipe\{pipeName}",
            "-map", "0:v", "-map", "1:a",
            "-c:v", "h264_mf", "-hw_encoding", "1", "-b:v", "4500k", "-g", (fps * 2).ToString(), "-pix_fmt", "nv12",
            "-fps_mode", "cfr", "-r", fps.ToString(),
            "-af", "aresample=async=1000", "-c:a", "aac", "-b:a", "128k",
            "-f", container, target,
        })
            info.ArgumentList.Add(arg);
        try
        {
            Process process = Process.Start(info);
            GD.Print(testFile != null ? $"DeskCiv stream: writing test file {testFile}" : "DeskCiv stream: started streaming to YouTube");
            return new StreamOutput(process, pipe, width, height);
        }
        catch (Exception e)
        {
            pipe.Dispose();
            GD.PushWarning($"DeskCiv stream: could not start ffmpeg ({e.Message})");
            return null;
        }
    }

    /// <summary>Queues the current frame; frames are dropped rather than stalling the game when ffmpeg falls behind.</summary>
    public void Push(Viewport viewport)
    {
        if (_ffmpeg.HasExited) return;
        Image image = viewport.GetTexture().GetImage();
        if (image.GetWidth() != _width || image.GetHeight() != _height) image.Resize(_width, _height, Image.Interpolation.Bilinear);
        if (image.GetFormat() != Image.Format.Rgba8) image.Convert(Image.Format.Rgba8);
        if (!_frames.TryAdd(image.GetData())) Dropped++;
    }

    /// <summary>Queues mixed stereo sound captured from the Master bus.</summary>
    public void PushSound(Vector2[] samples)
    {
        if (samples.Length == 0 || _ffmpeg.HasExited) return;
        var bytes = new byte[samples.Length * 8];
        for (int i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8), samples[i].X);
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 8 + 4), samples[i].Y);
        }
        _sound.TryAdd(bytes);
    }

    private void Write()
    {
        try
        {
            var stdin = _ffmpeg.StandardInput.BaseStream;
            foreach (byte[] frame in _frames.GetConsumingEnumerable())
                stdin.Write(frame, 0, frame.Length);
            stdin.Close();
        }
        catch (Exception)
        {
            // ffmpeg exited (network or key problem); the game keeps running.
        }
    }

    private void WriteSound()
    {
        try
        {
            _soundPipe.WaitForConnection();
            foreach (byte[] block in _sound.GetConsumingEnumerable())
                _soundPipe.Write(block, 0, block.Length);
        }
        catch (Exception)
        {
            // ffmpeg closed the pipe.
        }
        finally
        {
            _soundPipe.Dispose();
        }
    }

    public void Dispose()
    {
        _frames.CompleteAdding();
        _sound.CompleteAdding();
        _writer.Join(3000);
        _soundWriter.Join(3000);
        if (!_ffmpeg.WaitForExit(8000)) _ffmpeg.Kill();
    }
}

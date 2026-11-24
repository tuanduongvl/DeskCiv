using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Godot;

namespace DeskCiv;

/// <summary>
/// Sends the game's own rendered frames to ffmpeg, which encodes them on the GPU (Media Foundation)
/// and streams them by RTMP, or writes a file for testing. Only the game picture is sent, never the desktop.
/// The stream key is read from a file the user creates, so it never appears on a command line or in logs.
/// </summary>
public sealed class StreamOutput : IDisposable
{
    private const string YouTubeIngest = "rtmp://a.rtmp.youtube.com/live2/";
    private readonly Process _ffmpeg;
    private readonly BlockingCollection<byte[]> _frames = new(4);
    private readonly Thread _writer;
    private readonly int _width, _height;
    public int Dropped { get; private set; }

    private StreamOutput(Process ffmpeg, int width, int height)
    {
        _ffmpeg = ffmpeg;
        _width = width;
        _height = height;
        _writer = new Thread(Write) { IsBackground = true, Name = "stream-writer" };
        _writer.Start();
    }

    /// <param name="keyFile">File holding only the YouTube stream key, or null.</param>
    /// <param name="testFile">Instead of streaming, write an .flv file (for tests).</param>
    public static StreamOutput Start(string keyFile, string testFile, string ffmpegPath, int width, int height, int fps)
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
        var info = new ProcessStartInfo(ffmpegPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = false,
            CreateNoWindow = true,
        };
        foreach (string arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pix_fmt", "rgba", "-s", $"{width}x{height}", "-r", fps.ToString(), "-i", "-",
            "-f", "lavfi", "-i", "anullsrc=r=44100:cl=stereo",
            "-c:v", "h264_mf", "-hw_encoding", "1", "-b:v", "4500k", "-g", (fps * 2).ToString(), "-pix_fmt", "nv12",
            "-c:a", "aac", "-b:a", "128k", "-shortest", "-f", "flv", target,
        })
            info.ArgumentList.Add(arg);
        try
        {
            Process process = Process.Start(info);
            GD.Print(testFile != null ? $"DeskCiv stream: writing test file {testFile}" : "DeskCiv stream: started streaming to YouTube");
            return new StreamOutput(process, width, height);
        }
        catch (Exception e)
        {
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

    public void Dispose()
    {
        _frames.CompleteAdding();
        _writer.Join(3000);
        if (!_ffmpeg.WaitForExit(5000)) _ffmpeg.Kill();
    }
}

using System.Diagnostics;

namespace StarshipTelemetryExtractor
{
    public class FFmpegWrapper
    {
        public static async Task<int> RunFFmpegAsync(string arguments)
        {
            using (Process ffmpegProcess = new Process())
            {
                ffmpegProcess.StartInfo.FileName = "ffmpeg"; // Assuming ffmpeg is in your PATH
                ffmpegProcess.StartInfo.Arguments = arguments;

                ffmpegProcess.StartInfo.RedirectStandardOutput = true;
                ffmpegProcess.StartInfo.RedirectStandardError = true;
                ffmpegProcess.StartInfo.UseShellExecute = false;
                ffmpegProcess.StartInfo.CreateNoWindow = false;

                ffmpegProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"FFmpeg Output: {e.Data}");
                    }
                };
                ffmpegProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"FFmpeg Error: {e.Data}");
                    }
                };

                ffmpegProcess.Start();
                ffmpegProcess.BeginOutputReadLine();
                ffmpegProcess.BeginErrorReadLine();
                await ffmpegProcess.WaitForExitAsync();

                return ffmpegProcess.ExitCode;
            }
        }
    }
}

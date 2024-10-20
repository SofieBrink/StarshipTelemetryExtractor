using Patagames.Ocr;
using System.Numerics;

namespace StarshipTelemetryExtractor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string InputPath = args.Length > 0 ? args[0] : "C:\\Users\\Sofie\\Desktop\\tmpfolder\\frames";
            string OutputPath = args.Length > 1 ? args[1] : "C:\\Users\\Sofie\\Desktop\\IFT-5 Flight Data";

            DateTime startTime = DateTime.Now;
            Dictionary<string, Vector4> telemetryPositions;
            telemetryPositions = new Dictionary<string, Vector4>()
            {
                { "BoosterAltitude", new Vector4(358, 947, 90, 33) }
              , { "BoosterVelocity", new Vector4(358, 912, 90, 33) }
              , { "StarshipAltitude", new Vector4(1542, 947, 90, 33) }
              , { "StarshipVelocity", new Vector4(1542, 912, 90, 33) }
            };

            if (!Directory.Exists(InputPath)) { Console.WriteLine($"Inputpath: {InputPath} is invalid"); return; }
            if (!Directory.Exists(OutputPath)) { Console.WriteLine($"Outputpath: {OutputPath} is invalid"); return; }

            bool getFrames;
            if (Directory.GetDirectories(OutputPath).Contains(OutputPath + "\\Frames"))
            {
                Console.WriteLine("Found existing frames in folder, would you like to overwrite them? (y/N)");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(tmp) || tmp == "n") { getFrames = false; break; }
                    else if (tmp == "y") { getFrames = true; break; }
                    else Console.WriteLine("Invalid input!");
                }
                if (getFrames)
                {
                    Directory.Delete(OutputPath + "\\Frames", true);
                }
            }
            else { getFrames = true; }

            if (getFrames)
            {
                // logic to allow user to select telemetry data out of the video stream themselves.
                string videoFile = string.Empty;
                int startingSecond = 0;
                int secondsToGrab = 0;
                int fps = 1;

                Console.WriteLine("What video file should we get the data from?");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if (File.Exists(tmp)) { videoFile = tmp; break; }
                    else Console.WriteLine("File does not exist!");
                }
                Console.WriteLine("At what timestamp should we start the telemetry gathering? leave empty for 0.");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(tmp) || int.TryParse(tmp, out startingSecond)) break;
                    else Console.WriteLine("Invalid input!");
                }
                Console.WriteLine("How long should we gather telemetry for? (in seconds)");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if (int.TryParse(tmp, out secondsToGrab) && secondsToGrab > 0) break;
                    else Console.WriteLine("Invalid input!");
                }
                Console.WriteLine("At what framerate should we gather data? leave empty for 1fps");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if ((string.IsNullOrWhiteSpace(tmp) || int.TryParse(tmp, out fps)) && fps > 0) break;
                    else Console.WriteLine("Invalid input!");
                }

                List<Task<int>> ffmpegs = new List<Task<int>>();

                foreach (var telemPos in telemetryPositions)
                {
                    Directory.CreateDirectory(OutputPath + $"\\Frames\\{telemPos.Key}");
                    var ffmpegArgs = $"-ss 228 -i \"{videoFile}\" -vf \"crop={telemPos.Value.Z}:{telemPos.Value.W}:{telemPos.Value.X}:{telemPos.Value.Y},fps={fps}\" -t {secondsToGrab} \"{OutputPath}/Frames/{telemPos.Key}/frame_%04d.png\"";
                    ffmpegs.Add(FFmpegWrapper.RunFFmpegAsync(ffmpegArgs));
                }

                int[] results = Task.WhenAll(ffmpegs).GetAwaiter().GetResult();
                Console.WriteLine("FFmpeg finished processing frames");
            }
            

            var ocr = OcrApi.Create();
            ocr.Init();

            ScreenReader.GetTelemetryData(ocr, InputPath, out var rawData, out var correctedData);

            using (StreamWriter writer = new StreamWriter(OutputPath + "\\DataOutput.csv"))
            {
                writer.WriteLine("fileName,rawValue,correctedValue");

                for (int i = 0; i < rawData.Count; i++)
                {
                    string fileName = rawData[i].fileName;
                    string rawValue = rawData[i].value?.ToString() ?? "";
                    string correctedValue = correctedData[i].value?.ToString() ?? "";

                    writer.WriteLine($"{fileName},{rawValue},{correctedValue}");
                }
            }

            ocr.Dispose();

            Console.WriteLine($"Done, took {Math.Round((DateTime.Now - startTime).TotalSeconds, 1)}s including awaiting input.");
        }



        
    }
}

using Emgu.CV;
using Patagames.Ocr;
using System.Data;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace StarshipTelemetryExtractor
{
    public class TelemetryRecord
    {
        public List<(string fileName, int? value)> rawData { get; set; } = new List<(string, int?)>();
        public List<(string fileName, int? value)> correctedData { get; set; } = new List<(string, int?)>();
    }

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
            Dictionary<string, TelemetryRecord> TelemetryData;

            if (!Directory.Exists(InputPath)) { Console.WriteLine($"Inputpath: {InputPath} is invalid"); return; }
            if (!Directory.Exists(OutputPath)) { Console.WriteLine($"Outputpath: {OutputPath} is invalid"); return; }
            if (!File.Exists(OutputPath + "\\Output.log")) File.Delete(OutputPath + "\\Output.log");

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
                    Console.WriteLine("Removing old frames...");
                    Directory.Delete(OutputPath + "\\Frames", true);
                }
            }
            else { getFrames = true; }

            if (getFrames)
            {
                // TODO: logic to allow user to select telemetry data out of the video stream themselves.
                string videoFile = string.Empty;
                int startingSecond = 0;
                int secondsToGrab = 0;
                int fps = 30;

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
                // Just locking to 30fps for now, makes everthing easier.
                //Console.WriteLine("At what framerate should we gather data? leave empty for 1fps");
                //while (true)
                //{
                //    var tmp = Console.ReadLine();
                //    if ((string.IsNullOrWhiteSpace(tmp) || int.TryParse(tmp, out fps)) && fps > 0) break;
                //    else Console.WriteLine("Invalid input!");
                //}

                List<Task<int>> ffmpegs = new List<Task<int>>();

                foreach (var telemPos in telemetryPositions)
                {
                    Directory.CreateDirectory(OutputPath + $"\\Frames\\{telemPos.Key}");
                    var ffmpegArgs = $"-ss {startingSecond} -i \"{videoFile}\" -vf \"crop={telemPos.Value.Z}:{telemPos.Value.W}:{telemPos.Value.X}:{telemPos.Value.Y},fps={fps}\" -t {secondsToGrab} \"{OutputPath}/Frames/{telemPos.Key}/frame_%06d.png\"";
                    ffmpegs.Add(FFmpegWrapper.RunFFmpegAsync(ffmpegArgs));
                }

                int[] results = Task.WhenAll(ffmpegs).GetAwaiter().GetResult();
                Console.WriteLine("FFmpeg finished processing frames");
            }

            bool getRawData;
            if (Directory.GetFiles(OutputPath).Contains(OutputPath + "\\RawData.csv"))
            {
                Console.WriteLine("Found existing raw data in folder, would you like to overwrite it? (y/N)");
                while (true)
                {
                    var tmp = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(tmp) || tmp == "n") { getRawData = false; break; }
                    else if (tmp == "y") { getRawData = true; break; }
                    else Console.WriteLine("Invalid input!");
                }
                if (getRawData)
                {
                    File.Delete(OutputPath + "\\RawData.csv");
                }
            }
            else { getRawData = true; }

            if (getRawData)
            {
                var ocr = OcrApi.Create();
                ocr.Init();

                TelemetryData = new Dictionary<string, TelemetryRecord>();
                foreach (var folder in Directory.GetDirectories(OutputPath + "\\Frames"))
                {
                    var fileName = Path.GetFileName(folder);
                    ScreenReader.GetTelemetryData(ocr, folder, out var rawData, out var correctedData, out var tmpLog); // pls async
                    TelemetryData.Add(fileName, new TelemetryRecord { rawData = rawData, correctedData = correctedData });
                    using (StreamWriter writer = new StreamWriter(OutputPath + "\\Output.log", true))
                    {
                        writer.WriteLine(fileName + ":");
                        foreach (var row in tmpLog)
                        {
                            writer.WriteLine(row);
                        }
                    }
                }

                ocr.Dispose();

                Console.WriteLine("Formatting data...");
                FileIO.WriteToCSV(OutputPath + "\\RawData.csv", TelemetryData);
            }
            else
            {
                Console.WriteLine("Reading data...");
                TelemetryData = FileIO.ReadFromCSV(OutputPath + "\\RawData.csv");
            }

            Console.WriteLine($"Done, took {Math.Round((DateTime.Now - startTime).TotalSeconds, 1)}s including awaiting input.");
        }
    }
}

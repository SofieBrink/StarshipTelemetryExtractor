using Patagames.Ocr;

namespace StarshipTelemetryExtractor
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string InputPath = args.Length > 0 ? args[0] : "C:\\Users\\Sofie\\Desktop\\tmpfolder\\frames";
            string OutputPath = args.Length > 0 ? args[1] : "C:\\Users\\Sofie\\Desktop\\IFT-5 Flight Data";
            DateTime startTime = DateTime.Now;

            if (!Directory.Exists(InputPath)) { Console.WriteLine($"Inputpath: {InputPath} is invalid"); return; }
            if (!Directory.Exists(OutputPath)) { Console.WriteLine($"Outputpath: {OutputPath} is invalid"); return; }



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

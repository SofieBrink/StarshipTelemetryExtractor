using Emgu.CV.CvEnum;
using Emgu.CV;
using Patagames.Ocr;
using Patagames.Ocr.Enums;
using System.Drawing;
using Emgu.CV.Structure;
using System.Text.RegularExpressions;

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
            if (Directory.EnumerateFileSystemEntries(OutputPath).Any()) { Console.WriteLine($"Outputpath: {OutputPath} is not empty"); return; }
            var ocr = OcrApi.Create();
            ocr.Init();

            List<(string fileName, int? value)> rawData = new List<(string fileName, int? value)>();
            List<(string fileName, string? value)> failedData = new List<(string fileName, string? value)>();

            foreach (var entry in Directory.EnumerateFileSystemEntries(InputPath))
            {
                var fileName = entry.Split('\\').Last();
                Mat img = CvInvoke.Imread(entry, ImreadModes.Color);
                Mat greyImage = new Mat();
                CvInvoke.CvtColor(img, greyImage, ColorConversion.Bgr2Gray);
                Mat thresholded = new Mat();
                CvInvoke.Threshold(greyImage, thresholded, 200, 255, ThresholdType.Binary);
                Mat dilatedImage = new Mat();
                Mat kernel = CvInvoke.GetStructuringElement(ElementShape.Rectangle, new Size(1, 1), new Point(-1, -1));
                CvInvoke.Dilate(thresholded, dilatedImage, kernel, new Point(-1, -1), 2, BorderType.Default, new MCvScalar(1));
                Mat invertedImage = new Mat();
                CvInvoke.BitwiseNot(dilatedImage, invertedImage);
                Bitmap bitmap = invertedImage.ToBitmap();

                var plaintext = ocr.GetTextFromImage(bitmap);
                if (plaintext != null) plaintext = Regex.Replace(plaintext, @"\s+", "");

                Console.WriteLine($"Processing: {fileName}; Value: {plaintext}");

                if (int.TryParse(plaintext, out var output)) rawData.Add((fileName, output));
                else { rawData.Add((fileName, null)); failedData.Add((fileName, plaintext)); }
            }

            List<(string fileName, int? value)> correctedData = new List<(string fileName, int? value)>(rawData);

            if (failedData.Count > 0)
            {
                Console.WriteLine($"Failed to get {failedData.Count} value(s), attempting to correct.");
                CorrectMissingValues(rawData, ref correctedData);
                foreach (var entry in failedData)
                {
                    var correction = correctedData.FirstOrDefault(d => d.fileName == entry.fileName);
                    Console.WriteLine($"Corrected {entry.fileName} from: \"{entry.value}\" to: {correction.value}");
                }
            }

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

            Console.WriteLine($"Done, took {Math.Round((DateTime.Now - startTime).TotalSeconds, 1)}s");
        }



        static void CorrectMissingValues(List<(string fileName, int? value)> pRawData, ref List<(string fileName, int? value)> rCorrectedData)
        {
            int start = -1, end = -1;

            for (int i = 0; i < pRawData.Count; i++)
            {
                if (pRawData[i].value == null)
                {
                    if (start == -1) start = i;
                }
                else if (start != -1)
                {
                    end = i;
                    InterpolateValues(pRawData, ref rCorrectedData, start, end);
                    start = end = -1;
                }
            }
        }

        static void InterpolateValues(List<(string fileName, int? value)> pRawData, ref List<(string fileName, int? value)> rCorrectedData, int pStart, int pEnd)
        {
            int knownValueBefore = pStart >= 0 ? pRawData[pStart - 1].value!.Value : 0; 
            int knownValueAfter = pRawData[pEnd].value!.Value;
            int nullCount = pEnd - pStart;

            float step = (knownValueAfter - knownValueBefore) / (nullCount + 1);
            for (int i = 0; i < nullCount; i++)
            {
                rCorrectedData[pStart + i] = (pRawData[pStart + i].fileName, knownValueBefore + (int) (step * (i + 1)));
            }
        }
    }
}

using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV;
using Patagames.Ocr;
using System.Drawing;
using System.Text.RegularExpressions;

namespace StarshipTelemetryExtractor
{
    public class ScreenReader
    {
        public static (TelemetryRecord record, List<string> logLines) GetTelemetryData(OcrApi pOcr // make this async so we can analyse all threads at once
                                                                                        , string pPath)
        {
            var logLines = new List<string>();
            var returnRecord = new TelemetryRecord();
            List<(string fileName, string? value)> failedData = new List<(string fileName, string? value)>();

            if (!Directory.Exists(pPath)) return (new TelemetryRecord(), logLines);
            foreach (var entry in Directory.EnumerateFileSystemEntries(pPath))
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

                var plaintext = pOcr.GetTextFromImage(bitmap);
                if (plaintext != null) plaintext = Regex.Replace(plaintext, @"\s+", "");

                Console.WriteLine($"Processing: {fileName}; Value: {plaintext}");

                if (int.TryParse(plaintext, out var output)) returnRecord.rawData.Add((fileName, output));
                else { returnRecord.rawData.Add((fileName, null)); failedData.Add((fileName, plaintext)); }
            }

            returnRecord.correctedData = new List<(string fileName, int? value)>(returnRecord.rawData);

            if (failedData.Count > 0)
            {
                Console.WriteLine($"Failed to get {failedData.Count} value(s), attempting to correct.");
                CorrectMissingValues(ref returnRecord);
                foreach (var entry in failedData)
                {
                    var correction = returnRecord.correctedData.FirstOrDefault(d => d.fileName == entry.fileName);
                    if (correction.value != null) logLines.Add($"Corrected {entry.fileName} from: \"{entry.value}\" to: {correction.value}");
                }
            }
            returnRecord.correctedData.RemoveAll(item => item.value == null);

            HandleOutliers(ref returnRecord, ref logLines);

            return (returnRecord, logLines);
        }

        static void CorrectMissingValues(ref TelemetryRecord rTelemetryRecord)
        {
            int start = -1, end = -1;
            int validCount = 0;

            for (int i = 0; i < rTelemetryRecord.rawData.Count; i++)
            {
                if (rTelemetryRecord.rawData[i].value == null)
                {
                    if (start == -1) start = i;
                }
                else if (start != -1)
                {
                    if (start == 0 && validCount < 5)
                    {
                        validCount++;
                        continue;
                    }
                    else if (start == 0) validCount = 0;
                    end = i;
                    InterpolateValues(ref rTelemetryRecord, start, end);
                    start = end = -1;
                }
            }
        }

        static void InterpolateValues(ref TelemetryRecord rTelemetryRecord, int pStart, int pEnd)
        {
            int knownValueBefore = pStart > 0 && rTelemetryRecord.correctedData[pStart - 1].value != null ? rTelemetryRecord.correctedData[pStart - 1].value!.Value : 0;
            int knownValueAfter = rTelemetryRecord.correctedData[pEnd].value!.Value;
            int nullCount = pEnd - pStart;

            float step = (float) (knownValueAfter - knownValueBefore) / (nullCount + 1);
            for (int i = 0; i < nullCount; i++)
            {
                rTelemetryRecord.correctedData[pStart + i] = (rTelemetryRecord.correctedData[pStart + i].fileName, knownValueBefore + (int) (step * (i + 1)));
            }
        }

        static void HandleOutliers(ref TelemetryRecord rTelemetryRecord, ref List<string> rLogLines)
        {
            var originalData = new List<(string fileName, int? value)>(rTelemetryRecord.correctedData);

            List<int> lastValues = new List<int>();
            List<(int oldValue, int index)> outlierIndexes = new List<(int, int)>();

            double allowedErrorMarginMultiplier = 1;
            for (int i = 0; i < rTelemetryRecord.correctedData.Count; i++)
            {
                if (lastValues.Count == 0 && rTelemetryRecord.correctedData.Count > 1) { lastValues.Add((int) rTelemetryRecord.correctedData[i + 1].value! - (int) rTelemetryRecord.correctedData[i].value!); continue; } // if the first value is an outlier, there's nothing we can do.
                var movingAverage = lastValues.Average();
                int lastValidIndex = i - 1;
                while (lastValidIndex >= 0 && outlierIndexes.Any(outlier => outlier.index == lastValidIndex)) lastValidIndex--;
                if (lastValidIndex < 0) continue;
                var delta = ((int) rTelemetryRecord.correctedData[i].value! - (int) rTelemetryRecord.correctedData[lastValidIndex].value!);

                if (Math.Abs(Math.Abs(movingAverage) - Math.Abs(delta)) > Math.Max(Math.Abs(movingAverage * (i - lastValidIndex)), 1) * allowedErrorMarginMultiplier)
                {
                    outlierIndexes.Add(((int) originalData[i].value!, i));
                }
                else
                {
                    lastValues.Add(delta);
                }
                if (lastValues.Count > 30) lastValues.RemoveAt(0); // magic 30fps number, keep one second of data stored
            }

            int start = -1, end = -1;
            var outlierIndexesList = outlierIndexes.Select(i => i.index);

            for (int i = 0; i < rTelemetryRecord.correctedData.Count; i++)
            {
                if (outlierIndexesList.Contains(i))
                {
                    if (start == -1) start = i;
                }
                else if (start != -1)
                {
                    end = i;
                    InterpolateValues(ref rTelemetryRecord, start, end);
                    start = end = -1;
                }
            }
            if (outlierIndexes.Count > 0)
            {
                Console.WriteLine($"Found {outlierIndexes.Count} outlier(s), attempting to correct.");
                foreach (var i in outlierIndexes)
                {
                    rLogLines.Add($"Corrected {rTelemetryRecord.correctedData[i.index].fileName} from: \"{i.oldValue}\" to: {rTelemetryRecord.correctedData[i.index].value}");
                }
            }
        }
    }
}

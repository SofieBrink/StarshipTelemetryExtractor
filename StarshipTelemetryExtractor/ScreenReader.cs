using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV;
using System.Drawing;
using System.Text.RegularExpressions;
using Tesseract;

namespace StarshipTelemetryExtractor
{
    public class ScreenReader
    {
        public static (TelemetryRecord record, List<string> logLines) GetTelemetryData(TesseractEngine pOcr // make this async so we can analyse all threads at once
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

                string plaintext;
                using (var imgPix = PixConverter.ToPix(bitmap))
                {
                    using (var page = pOcr.Process(imgPix, PageSegMode.SingleLine))
                    {
                        plaintext = page.GetText();
                    }
                }
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
            Queue<int> lastValues = new Queue<int>(30); // magic 30fps number, keep one second of data stored
            List<(int oldValue, int index)> outlierIndexes = new List<(int, int)>();
            HashSet<int> outlierIndexesSet = new HashSet<int>();

            double allowedErrorMarginMultiplier = 1;

            for (int i = 0; i < rTelemetryRecord.correctedData.Count; i++)
            {
                if (lastValues.Count == 0 && rTelemetryRecord.correctedData.Count > 1)
                {
                    lastValues.Enqueue((int) rTelemetryRecord.correctedData[i + 1].value! - (int) rTelemetryRecord.correctedData[i].value!);
                    continue; // Cannot determine outliers for the first value
                }

                double movingAverage = lastValues.Average();

                int lastValidIndex = i - 1;
                while (lastValidIndex >= 0 && outlierIndexesSet.Contains(lastValidIndex))
                {
                    lastValidIndex--;
                }

                if (lastValidIndex < 0) continue;

                int delta = (int)rTelemetryRecord.correctedData[i].value! - (int)rTelemetryRecord.correctedData[lastValidIndex].value!;

                if (Math.Abs(Math.Abs(movingAverage) - Math.Abs(delta)) > Math.Max(Math.Abs(movingAverage * (i - lastValidIndex)), 1) * allowedErrorMarginMultiplier)
                {
                    outlierIndexes.Add(((int) rTelemetryRecord.correctedData[i].value!, i));
                    outlierIndexesSet.Add(i);
                }
                else
                {
                    lastValues.Enqueue(delta);
                }

                if (lastValues.Count > 30) lastValues.Dequeue();
            }

            int start = -1, end = -1;
            foreach (var i in Enumerable.Range(0, rTelemetryRecord.correctedData.Count))
            {
                if (outlierIndexesSet.Contains(i))
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

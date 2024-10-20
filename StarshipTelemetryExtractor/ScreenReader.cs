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
        public static void GetTelemetryData(OcrApi pOcr
                                          , string pPath
                                          , out List<(string fileName, int? value)> oRawData
                                          , out List<(string fileName, int? value)> oCorrectedData)
        {
            oRawData = new List<(string fileName, int? value)>();
            oCorrectedData = new List<(string fileName, int? value)>();
            List<(string fileName, string? value)> failedData = new List<(string fileName, string? value)>();

            if (!Directory.Exists(pPath)) return;
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

                if (int.TryParse(plaintext, out var output)) oRawData.Add((fileName, output));
                else { oRawData.Add((fileName, null)); failedData.Add((fileName, plaintext)); }
            }

            oCorrectedData = new List<(string fileName, int? value)>(oRawData);

            if (failedData.Count > 0)
            {
                Console.WriteLine($"Failed to get {failedData.Count} value(s), attempting to correct.");
                CorrectMissingValues(oRawData, ref oCorrectedData);
                foreach (var entry in failedData)
                {
                    var correction = oCorrectedData.FirstOrDefault(d => d.fileName == entry.fileName);
                    if (correction.value != null) Console.WriteLine($"Corrected {entry.fileName} from: \"{entry.value}\" to: {correction.value}");
                }
            }
            oCorrectedData.RemoveAll(item => item.value == null);

            HandleOutliers(oCorrectedData, ref oCorrectedData);
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

        static void HandleOutliers(List<(string fileName, int? value)> pRawData, ref List<(string fileName, int? value)> rCorrectedData)
        {
            List<int> lastValues = new List<int>();
            List<(int oldValue, int index)> outlierIndexes = new List<(int, int)>();

            double allowedErrorMarginMultiplier = 10;
            for (int i = 0; i < pRawData.Count; i++)
            {
                if (lastValues.Count == 0 && pRawData.Count > 1) { lastValues.Add((int) pRawData[i + 1].value! - (int) pRawData[i].value!); continue; } // if the first value is an outlier, there's nothing we can do.
                var movingAverage = lastValues.Average();
                int lastValidIndex = i - 1;
                while (lastValidIndex >= 0 && outlierIndexes.Any(outlier => outlier.index == lastValidIndex)) lastValidIndex--;
                if (lastValidIndex < 0) continue;
                var delta = ((int) pRawData[i].value! - (int) pRawData[lastValidIndex].value!) / (i - lastValidIndex);

                if (Math.Abs(Math.Abs(movingAverage) - Math.Abs(delta)) > Math.Max(Math.Abs(movingAverage), 0.5) * allowedErrorMarginMultiplier)
                {
                    outlierIndexes.Add(((int) pRawData[i].value!, i));
                }
                else
                {
                    lastValues.Add(delta);
                }
                if (lastValues.Count > 30) lastValues.RemoveAt(0); // magic 30fps number, keep one second of data stored
            }

            int start = -1, end = -1;
            var outlierIndexesList = outlierIndexes.Select(i => i.index);

            for (int i = 0; i < pRawData.Count; i++)
            {
                if (outlierIndexesList.Contains(i))
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
            if (outlierIndexes.Count > 0)
            {
                Console.WriteLine($"Found {outlierIndexes.Count} outlier(s), attempting to correct.");
                foreach (var i in outlierIndexes)
                {
                    Console.WriteLine($"Corrected {pRawData[i.index].fileName} from: \"{i.oldValue}\" to: {rCorrectedData[i.index].value}");
                }
            }
        }
    }
}

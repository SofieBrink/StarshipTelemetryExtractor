using System.Collections.Concurrent;
using System.Text;

namespace StarshipTelemetryExtractor
{
    public class FileIO
    {
        //TODO: fix these methods, they're both bigger than they should be, do more than they should, and aren't described by their names properly.
        public static void WriteToCSV(string pFilePath, Dictionary<string, TelemetryRecord> pTelemetry)
        {
            var headerRow = new StringBuilder("MissionTime, FileName");
            var rows = new ConcurrentBag<string>();
            var uniqueFileNames = pTelemetry
                .SelectMany(data => data.Value.rawData.Concat(data.Value.correctedData))
                .Select(file => file.fileName)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            var rawLookup = pTelemetry.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.rawData.ToDictionary(data => data.fileName, data => data.value)
            );

            var correctedLookup = pTelemetry.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.correctedData.ToDictionary(data => data.fileName, data => data.value)
            );

            foreach (var kvp in pTelemetry)
            {
                headerRow.Append($", {kvp.Key}_Raw");
                headerRow.Append($", {kvp.Key}_Corrected");
            }

            Parallel.ForEach(uniqueFileNames, fileName =>
            {
                var row = new StringBuilder($"=(ROW()-1)/(30*86400), {fileName}");
                foreach (var kvp in pTelemetry)
                {
                    var rawValue = rawLookup[kvp.Key].TryGetValue(fileName, out var raw) ? raw : null;
                    var correctedValue = correctedLookup[kvp.Key].TryGetValue(fileName, out var corrected) ? corrected : null;

                    row.Append($", {rawValue}");
                    row.Append($", {correctedValue}");
                }
                rows.Add(row.ToString());
            });

            Console.WriteLine($"Writing data to {pFilePath.Split("\\").Last()}...");
            using (StreamWriter writer = new StreamWriter(pFilePath))
            {
                writer.WriteLine(headerRow);
                foreach (var row in rows.OrderBy(r => r))
                {
                    writer.WriteLine(row);
                }
            }
        }


        public static Dictionary<string, TelemetryRecord> ReadFromCSV(string pFilePath)
        {
            var returnDict = new Dictionary<string, TelemetryRecord>();
            var lines = File.ReadAllLines(pFilePath);
            var header = lines[0].Split(',').Select(h => h.Trim()).ToList();

            var telemetryKeys = new List<string>();
            for (int i = 1; i < header.Count; i += 2)
            {
                string telemetryKey = header[i].Replace("_Raw", "");
                telemetryKeys.Add(telemetryKey);
                if (!returnDict.ContainsKey(telemetryKey))
                {
                    returnDict[telemetryKey] = new TelemetryRecord();
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                var values = lines[i].Split(',').Select(v => v.Trim()).ToList();
                var fileName = values[0];

                for (int j = 0; j < telemetryKeys.Count; j++)
                {
                    var telemetryKey = telemetryKeys[j];
                    var rawDataValue = values[1 + j * 2];
                    var correctedDataValue = values[2 + j * 2];

                    if (!string.IsNullOrWhiteSpace(rawDataValue)) returnDict[telemetryKey].rawData.Add((fileName, Convert.ToInt32(rawDataValue)));
                    if (!string.IsNullOrWhiteSpace(correctedDataValue)) returnDict[telemetryKey].correctedData.Add((fileName, Convert.ToInt32(correctedDataValue)));
                }
            }

            return returnDict;
        }
    }
}

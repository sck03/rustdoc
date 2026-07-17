using System;
#nullable enable
using System.Collections.Generic;
using System.IO;

namespace ExportDocManager.Services.Tools
{
    internal sealed class PaddleOcrOnnxModelBundle
    {
        private readonly string _detDir;
        private readonly string _recDir;

        public PaddleOcrOnnxModelBundle(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                throw new ArgumentException("OCR 模型目录不能为空。", nameof(basePath));
            }

            BasePath = basePath;
            _detDir = Path.Combine(basePath, "det");
            _recDir = Path.Combine(basePath, "rec");
        }

        public string BasePath { get; }

        public string DetectionModelPath => Path.Combine(_detDir, "inference.onnx");

        public string RecognitionModelPath => Path.Combine(_recDir, "inference.onnx");

        public IReadOnlyList<string> LoadRecognitionLabels()
        {
            EnsureModelBundle();
            return LoadRecognitionLabels(_recDir);
        }

        public void EnsureModelBundle()
        {
            var missingFiles = new List<string>();
            AddMissingFile(missingFiles, DetectionModelPath);
            AddMissingFile(missingFiles, Path.Combine(_detDir, "inference.yml"));
            AddMissingFile(missingFiles, RecognitionModelPath);
            AddMissingFile(missingFiles, Path.Combine(_recDir, "inference.yml"));

            if (missingFiles.Count > 0)
            {
                throw new DirectoryNotFoundException(
                    "未找到 PP-OCRv6 Small ONNX 模型文件。请确认 det / rec 目录都包含 inference.onnx 与 inference.yml。缺失文件："
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, missingFiles));
            }
        }

        internal static IReadOnlyList<string> LoadRecognitionLabels(string recDir)
        {
            string ymlPath = Path.Combine(recDir, "inference.yml");
            if (!File.Exists(ymlPath))
            {
                throw new FileNotFoundException("未找到 PP-OCRv6 识别模型配置。", ymlPath);
            }

            var labels = ParseCharacterDictionary(File.ReadLines(ymlPath));
            if (labels.Count == 0)
            {
                throw new InvalidDataException($"PP-OCRv6 识别模型配置中缺少 character_dict：{ymlPath}");
            }

            return labels;
        }

        internal static IReadOnlyList<string> ParseCharacterDictionary(IEnumerable<string> ymlLines)
        {
            var labels = new List<string>();
            bool inDictionary = false;

            foreach (string rawLine in ymlLines)
            {
                string trimmed = rawLine.Trim();
                if (!inDictionary)
                {
                    inDictionary = string.Equals(trimmed, "character_dict:", StringComparison.Ordinal);
                    continue;
                }

                string itemLine = rawLine.TrimStart(' ', '\t');
                if (itemLine.Length == 0 || itemLine.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!itemLine.StartsWith("-", StringComparison.Ordinal))
                {
                    break;
                }

                string value = itemLine[1..];
                if (value.StartsWith(" ", StringComparison.Ordinal))
                {
                    value = value[1..];
                }

                labels.Add(ParseYamlScalar(value));
            }

            return labels;
        }

        private static void AddMissingFile(List<string> missingFiles, string path)
        {
            if (!File.Exists(path))
            {
                missingFiles.Add(path);
            }
        }

        private static string ParseYamlScalar(string value)
        {
            string trimmed = TrimAsciiWhitespace(value);
            if (trimmed.Length >= 2 && trimmed[0] == '\'' && trimmed[^1] == '\'')
            {
                return trimmed[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            {
                return trimmed[1..^1]
                    .Replace("\\\"", "\"", StringComparison.Ordinal)
                    .Replace("\\\\", "\\", StringComparison.Ordinal);
            }

            return trimmed;
        }

        private static string TrimAsciiWhitespace(string value)
        {
            int start = 0;
            int end = value.Length - 1;

            while (start <= end && IsAsciiWhitespace(value[start]))
            {
                start++;
            }

            while (end >= start && IsAsciiWhitespace(value[end]))
            {
                end--;
            }

            return start > end ? string.Empty : value[start..(end + 1)];
        }

        private static bool IsAsciiWhitespace(char value) =>
            value == ' ' || value == '\t' || value == '\r' || value == '\n';
    }
}

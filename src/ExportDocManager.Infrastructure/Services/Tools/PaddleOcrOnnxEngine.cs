using System;
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace ExportDocManager.Services.Tools
{
    internal readonly record struct OcrRecognitionResult(string Text, float Score);

    internal sealed class PaddleOcrOnnxEngine : IDisposable
    {
        private const int DetectionMaxSide = 960;
        private const float DetectionBinaryThreshold = 0.20f;
        private const float DetectionBoxThreshold = 0.45f;
        private const float DetectionUnclipRatio = 1.40f;
        private const int RecognitionHeight = 48;
        private const int RecognitionMaxWidth = 3200;

        private static readonly float[] DetectionMean = [0.485f, 0.456f, 0.406f];
        private static readonly float[] DetectionStd = [0.229f, 0.224f, 0.225f];

        private readonly InferenceSession _detSession;
        private readonly InferenceSession _recSession;
        private readonly string _detInputName;
        private readonly string _recInputName;
        private readonly IReadOnlyList<string> _labels;

        public PaddleOcrOnnxEngine(PaddleOcrOnnxModelBundle modelBundle)
        {
            modelBundle.EnsureModelBundle();
            _detSession = CreateSession(modelBundle.DetectionModelPath);
            _recSession = CreateSession(modelBundle.RecognitionModelPath);
            _labels = NormalizeRecognitionLabels(modelBundle.LoadRecognitionLabels(), GetRecognitionClassCount(_recSession), modelBundle.BasePath);
            _detInputName = _detSession.InputMetadata.Keys.First();
            _recInputName = _recSession.InputMetadata.Keys.First();
        }

        public RotatedRect[] Detect(Mat src)
        {
            using var resized = ResizeForDetection(src);
            var input = CreateDetectionTensor(resized);
            var inputValue = NamedOnnxValue.CreateFromTensor(_detInputName, input);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _detSession.Run([inputValue]);
            Tensor<float> output = outputs.First().AsTensor<float>();
            int mapHeight = output.Dimensions[^2];
            int mapWidth = output.Dimensions[^1];
            float[] map = output.ToArray();

            using var binary = CreateBinaryMap(map, mapWidth, mapHeight);
            Cv2.FindContours(binary, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

            var regions = new List<RotatedRect>();
            foreach (OpenCvSharp.Point[] contour in contours)
            {
                if (contour.Length < 3)
                {
                    continue;
                }

                Rect mapRect = ClipRect(Cv2.BoundingRect(contour), mapWidth, mapHeight);
                if (mapRect.Width < 3 || mapRect.Height < 3)
                {
                    continue;
                }

                float score = CalculateBoxScore(map, binary, mapRect, mapWidth);
                if (score < DetectionBoxThreshold)
                {
                    continue;
                }

                Rect originalRect = MapToOriginalRect(mapRect, src.Width, src.Height, mapWidth, mapHeight);
                originalRect = ExpandRect(originalRect, src.Width, src.Height, DetectionUnclipRatio);
                if (originalRect.Width < 3 || originalRect.Height < 3)
                {
                    continue;
                }

                regions.Add(new RotatedRect(
                    new Point2f(originalRect.X + originalRect.Width / 2f, originalRect.Y + originalRect.Height / 2f),
                    new Size2f(originalRect.Width, originalRect.Height),
                    0));
            }

            return regions
                .OrderBy(region => region.Center.Y)
                .ThenBy(region => region.Center.X)
                .ToArray();
        }

        public OcrRecognitionResult Recognize(Mat src)
        {
            using var resized = ResizeForRecognition(src);
            var input = CreateRecognitionTensor(resized);
            var inputValue = NamedOnnxValue.CreateFromTensor(_recInputName, input);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _recSession.Run([inputValue]);
            Tensor<float> output = outputs.First().AsTensor<float>();
            return DecodeRecognition(output.ToArray(), output.Dimensions.ToArray());
        }

        public void Dispose()
        {
            _detSession.Dispose();
            _recSession.Dispose();
        }

        private static InferenceSession CreateSession(string modelPath)
        {
            using var options = new SessionOptions
            {
                IntraOpNumThreads = Environment.ProcessorCount > 4 ? 4 : 2,
                InterOpNumThreads = 1,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };
            return new InferenceSession(modelPath, options);
        }

        private static IReadOnlyList<string> NormalizeRecognitionLabels(
            IReadOnlyList<string> labels,
            int classCount,
            string modelBasePath)
        {
            if (classCount <= 0 || classCount == labels.Count + 1)
            {
                return labels;
            }

            if (classCount == labels.Count + 2 && !labels.Contains(" ", StringComparer.Ordinal))
            {
                return labels.Concat([" "]).ToArray();
            }

            throw new InvalidDataException(
                $"PP-OCRv6 识别模型类别数与字典不匹配。模型类别数：{classCount}，字典字符数：{labels.Count}，模型目录：{modelBasePath}");
        }

        private static int GetRecognitionClassCount(InferenceSession session)
        {
            int[] outputDimensions = session.OutputMetadata.Values.First().Dimensions.ToArray();
            if (outputDimensions.Length == 0)
            {
                return 0;
            }

            return outputDimensions[^1];
        }

        private static Mat ResizeForDetection(Mat src)
        {
            int width = src.Width;
            int height = src.Height;
            float scale = Math.Min(1f, DetectionMaxSide / (float)Math.Max(width, height));
            int resizedWidth = RoundToMultipleOf32(Math.Max(32, (int)Math.Round(width * scale)));
            int resizedHeight = RoundToMultipleOf32(Math.Max(32, (int)Math.Round(height * scale)));

            var resized = new Mat();
            Cv2.Resize(src, resized, new OpenCvSharp.Size(resizedWidth, resizedHeight), 0, 0, InterpolationFlags.Linear);
            return resized;
        }

        private static Mat ResizeForRecognition(Mat src)
        {
            int resizedWidth = Math.Clamp((int)Math.Ceiling(src.Width * (RecognitionHeight / (double)Math.Max(1, src.Height))), 16, RecognitionMaxWidth);
            var resized = new Mat();
            Cv2.Resize(src, resized, new OpenCvSharp.Size(resizedWidth, RecognitionHeight), 0, 0, InterpolationFlags.Linear);
            return resized;
        }

        private static DenseTensor<float> CreateDetectionTensor(Mat src)
        {
            int height = src.Height;
            int width = src.Width;
            var tensor = new DenseTensor<float>([1, 3, height, width]);
            var rows = src.AsRows<Vec3b>();
            for (int y = 0; y < height; y++)
            {
                Span<Vec3b> row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    Vec3b pixel = row[x];
                    tensor[0, 0, y, x] = NormalizeImageValue(pixel.Item0, DetectionMean[0], DetectionStd[0]);
                    tensor[0, 1, y, x] = NormalizeImageValue(pixel.Item1, DetectionMean[1], DetectionStd[1]);
                    tensor[0, 2, y, x] = NormalizeImageValue(pixel.Item2, DetectionMean[2], DetectionStd[2]);
                }
            }

            return tensor;
        }

        private static DenseTensor<float> CreateRecognitionTensor(Mat src)
        {
            int height = src.Height;
            int width = src.Width;
            var tensor = new DenseTensor<float>([1, 3, height, width]);
            var rows = src.AsRows<Vec3b>();
            for (int y = 0; y < height; y++)
            {
                Span<Vec3b> row = rows[y];
                for (int x = 0; x < width; x++)
                {
                    Vec3b pixel = row[x];
                    tensor[0, 0, y, x] = NormalizeRecognitionValue(pixel.Item0);
                    tensor[0, 1, y, x] = NormalizeRecognitionValue(pixel.Item1);
                    tensor[0, 2, y, x] = NormalizeRecognitionValue(pixel.Item2);
                }
            }

            return tensor;
        }

        private static Mat CreateBinaryMap(float[] map, int width, int height)
        {
            byte[] bytes = new byte[width * height];
            for (int i = 0; i < bytes.Length && i < map.Length; i++)
            {
                bytes[i] = map[i] > DetectionBinaryThreshold ? (byte)255 : (byte)0;
            }

            return Mat.FromPixelData(height, width, MatType.CV_8UC1, bytes).Clone();
        }

        private static float CalculateBoxScore(float[] map, Mat binary, Rect rect, int mapWidth)
        {
            var binaryRows = binary.AsRows<byte>();
            double sum = 0;
            int count = 0;
            for (int y = rect.Y; y < rect.Bottom; y++)
            {
                Span<byte> binaryRow = binaryRows[y];
                for (int x = rect.X; x < rect.Right; x++)
                {
                    if (binaryRow[x] == 0)
                    {
                        continue;
                    }

                    sum += map[y * mapWidth + x];
                    count++;
                }
            }

            return count == 0 ? 0 : (float)(sum / count);
        }

        private static Rect MapToOriginalRect(Rect mapRect, int originalWidth, int originalHeight, int mapWidth, int mapHeight)
        {
            int x = (int)Math.Floor(mapRect.X * originalWidth / (double)mapWidth);
            int y = (int)Math.Floor(mapRect.Y * originalHeight / (double)mapHeight);
            int right = (int)Math.Ceiling(mapRect.Right * originalWidth / (double)mapWidth);
            int bottom = (int)Math.Ceiling(mapRect.Bottom * originalHeight / (double)mapHeight);
            return ClipRect(new Rect(x, y, right - x, bottom - y), originalWidth, originalHeight);
        }

        private static Rect ExpandRect(Rect rect, int maxWidth, int maxHeight, float ratio)
        {
            double expandX = rect.Width * (ratio - 1) / 2.0d;
            double expandY = rect.Height * (ratio - 1) / 2.0d;
            int x = (int)Math.Floor(rect.X - expandX);
            int y = (int)Math.Floor(rect.Y - expandY);
            int right = (int)Math.Ceiling(rect.Right + expandX);
            int bottom = (int)Math.Ceiling(rect.Bottom + expandY);
            return ClipRect(new Rect(x, y, right - x, bottom - y), maxWidth, maxHeight);
        }

        private OcrRecognitionResult DecodeRecognition(float[] output, int[] dimensions)
        {
            if (dimensions.Length < 2)
            {
                return new OcrRecognitionResult(string.Empty, 0);
            }

            int sequenceLength = dimensions.Length == 3 ? dimensions[1] : dimensions[0];
            int classCount = dimensions[^1];
            bool hasBlankPrefix = classCount == _labels.Count + 1;
            int lastIndex = -1;
            float scoreSum = 0;
            int scoreCount = 0;
            var text = new StringBuilder();

            for (int t = 0; t < sequenceLength; t++)
            {
                int rowOffset = t * classCount;
                int maxIndex = 0;
                float maxScore = float.MinValue;
                for (int c = 0; c < classCount; c++)
                {
                    float score = output[rowOffset + c];
                    if (score > maxScore)
                    {
                        maxScore = score;
                        maxIndex = c;
                    }
                }

                if (maxIndex == lastIndex)
                {
                    continue;
                }

                lastIndex = maxIndex;
                if (hasBlankPrefix && maxIndex == 0)
                {
                    continue;
                }

                int labelIndex = hasBlankPrefix ? maxIndex - 1 : maxIndex;
                if (labelIndex < 0 || labelIndex >= _labels.Count)
                {
                    continue;
                }

                text.Append(_labels[labelIndex]);
                scoreSum += maxScore;
                scoreCount++;
            }

            return new OcrRecognitionResult(text.ToString(), scoreCount == 0 ? 0 : scoreSum / scoreCount);
        }

        private static Rect ClipRect(Rect rect, int maxWidth, int maxHeight)
        {
            int x = Math.Clamp(rect.X, 0, maxWidth);
            int y = Math.Clamp(rect.Y, 0, maxHeight);
            int right = Math.Clamp(rect.Right, x, maxWidth);
            int bottom = Math.Clamp(rect.Bottom, y, maxHeight);
            return new Rect(x, y, Math.Max(0, right - x), Math.Max(0, bottom - y));
        }

        private static int RoundToMultipleOf32(int value) => Math.Max(32, (int)Math.Round(value / 32.0d) * 32);

        private static float NormalizeImageValue(byte value, float mean, float std) => ((value / 255f) - mean) / std;

        private static float NormalizeRecognitionValue(byte value) => (value / 255f - 0.5f) / 0.5f;
    }
}

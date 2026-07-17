using System;
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ExportDocManager.Services.Infrastructure;
using OpenCvSharp;
using Serilog;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.Tools
{
    public class PaddleOcrService : IOcrService
    {
        private static PaddleOcrOnnxEngine? _ocrEngine;
        private static string? _initializedModelBasePath;
        private static readonly object _lock = new object();
        private static readonly bool PersistSuccessfulDiagnostics = ShouldPersistSuccessfulDiagnostics();

        private readonly string _modelBasePath;
        private readonly string _ocrDebugPath;

        public PaddleOcrService(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);

            _modelBasePath = Path.Combine(pathProvider.OcrModelRoot, "PaddleOCR", "V6");
            _ocrDebugPath = Path.Combine(pathProvider.LogRoot, "ocr-debug");
        }

        public Task<OcrResult> RecognizeAsync(Stream imageStream)
        {
            return Task.Run(() =>
            {
                byte[] buffer;
                if (imageStream is MemoryStream memoryStream)
                {
                    buffer = memoryStream.ToArray();
                }
                else
                {
                    using var copiedStream = new MemoryStream();
                    imageStream.CopyTo(copiedStream);
                    buffer = copiedStream.ToArray();
                }

                EnsureInitialized(_modelBasePath);

                using Mat src = Cv2.ImDecode(buffer, ImreadModes.Color);
                if (src.Empty())
                {
                    throw new InvalidOperationException("OCR 无法解码当前图片。");
                }

                string sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                List<string> diagnostics = new();
                OcrResult? bestResult = TryRecognizeByDetectedRegions(src, diagnostics, _modelBasePath);
                if (bestResult == null)
                {
                    bestResult = TryRecognizeWholeImageFallback(src, diagnostics);
                }

                if (bestResult == null || string.IsNullOrWhiteSpace(bestResult.FullText))
                {
                    PersistDiagnostics(sessionId, diagnostics, src, _ocrDebugPath);
                    throw new InvalidOperationException("OCR 未返回任何结果。");
                }

                diagnostics.Add($"final-text={SanitizeLogText(bestResult.FullText)}");
                if (PersistSuccessfulDiagnostics)
                {
                    PersistDiagnostics(sessionId, diagnostics, src, _ocrDebugPath);
                }

                return bestResult;
            });
        }

        private static OcrResult? TryRecognizeByDetectedRegions(Mat src, List<string> diagnostics, string modelBasePath)
        {
            try
            {
                OpenCvSharp.RotatedRect[] regions = RunDetector(src, modelBasePath);
                diagnostics.Add($"detector-regions={regions.Length}");
                if (regions.Length == 0)
                {
                    return null;
                }

                List<OpenCvSharp.Rect> mergedRects = MergeRegionsByLine(regions);
                diagnostics.Add($"merged-line-regions={mergedRects.Count}");
                var lines = new List<OcrLine>();
                for (int regionIndex = 0; regionIndex < mergedRects.Count; regionIndex++)
                {
                    OpenCvSharp.Rect mergedRect = ExpandRect(mergedRects[regionIndex], src.Width, src.Height, 10);
                    using var crop = new Mat(src, mergedRect);
                    string? bestText = null;
                    float bestRecScore = float.MinValue;

                    foreach ((string candidateName, Mat candidate) in CreateRecognitionCandidates(crop))
                    {
                        using (candidate)
                        {
                            var rec = TryRunRecognizerCandidate(candidateName, candidate, diagnostics);
                            if (!rec.HasValue)
                            {
                                continue;
                            }

                            var recValue = rec.Value;
                            float normalizedScore = NormalizeRecognizerScore(recValue.Score);
                            int textScore = ScoreRecognizedText(recValue.Text);
                            diagnostics.Add($"region={regionIndex}, rec-candidate={candidateName}, score={normalizedScore}, textScore={textScore}, textLength={(recValue.Text?.Length ?? 0)}, text={SanitizeLogText(recValue.Text)}");
                            if (!string.IsNullOrWhiteSpace(recValue.Text) && (textScore > 0) &&
                                (textScore > ScoreRecognizedText(bestText) || (textScore == ScoreRecognizedText(bestText) && normalizedScore > bestRecScore)))
                            {
                                bestRecScore = normalizedScore;
                                bestText = recValue.Text;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(bestText))
                    {
                        continue;
                    }

                    lines.Add(new OcrLine
                    {
                        Text = bestText,
                        X = mergedRect.X,
                        Y = mergedRect.Y,
                        Width = mergedRect.Width,
                        Height = mergedRect.Height
                    });
                }

                if (lines.Count == 0)
                {
                    return null;
                }

                return new OcrResult
                {
                    FullText = string.Join(Environment.NewLine, lines.OrderBy(line => line.Y).ThenBy(line => line.X).Select(line => line.Text)),
                    Lines = lines
                };
            }
            catch (Exception ex)
            {
                diagnostics.Add($"detector-stage-failed={ex.GetType().Name}, message={ex.Message}");
                Log.Warning(ex, "OCR detector/region pipeline failed.");
                return null;
            }
        }

        private static List<OpenCvSharp.Rect> MergeRegionsByLine(OpenCvSharp.RotatedRect[] regions)
        {
            var ordered = regions
                .Select(region => region.BoundingRect())
                .OrderBy(rect => rect.Y)
                .ThenBy(rect => rect.X)
                .ToList();

            var merged = new List<OpenCvSharp.Rect>();
            foreach (OpenCvSharp.Rect rect in ordered)
            {
                bool added = false;
                for (int i = 0; i < merged.Count; i++)
                {
                    OpenCvSharp.Rect existing = merged[i];
                    int verticalCenterDistance = Math.Abs((existing.Y + existing.Height / 2) - (rect.Y + rect.Height / 2));
                    int tolerance = Math.Max(18, Math.Min(existing.Height, rect.Height));
                    if (verticalCenterDistance <= tolerance)
                    {
                        merged[i] = UnionRect(existing, rect);
                        added = true;
                        break;
                    }
                }

                if (!added)
                {
                    merged.Add(rect);
                }
            }

            return merged
                .OrderBy(rect => rect.Y)
                .ThenBy(rect => rect.X)
                .ToList();
        }

        private static OpenCvSharp.Rect UnionRect(OpenCvSharp.Rect left, OpenCvSharp.Rect right)
        {
            int x = Math.Min(left.X, right.X);
            int y = Math.Min(left.Y, right.Y);
            int rightEdge = Math.Max(left.Right, right.Right);
            int bottomEdge = Math.Max(left.Bottom, right.Bottom);
            return new OpenCvSharp.Rect(x, y, rightEdge - x, bottomEdge - y);
        }

        private static OpenCvSharp.Rect ExpandRect(OpenCvSharp.Rect rect, int maxWidth, int maxHeight, int padding)
        {
            int x = Math.Max(0, rect.X - padding);
            int y = Math.Max(0, rect.Y - padding);
            int right = Math.Min(maxWidth, rect.Right + padding);
            int bottom = Math.Min(maxHeight, rect.Bottom + padding);
            return new OpenCvSharp.Rect(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
        }

        private static OcrResult? TryRecognizeWholeImageFallback(Mat src, List<string> diagnostics)
        {
            foreach ((string candidateName, Mat candidate) in CreateRecognitionCandidates(src))
            {
                using (candidate)
                {
                    var rec = TryRunRecognizerCandidate($"whole-{candidateName}", candidate, diagnostics);
                    if (rec.HasValue && !string.IsNullOrWhiteSpace(rec.Value.Text))
                    {
                        return new OcrResult
                        {
                            FullText = rec.Value.Text,
                            Lines =
                            [
                                new OcrLine
                                {
                                    Text = rec.Value.Text,
                                    X = 0,
                                    Y = 0,
                                    Width = src.Width,
                                    Height = src.Height
                                }
                            ]
                        };
                    }
                }
            }

            return null;
        }

        private static OpenCvSharp.RotatedRect[] RunDetector(Mat src, string modelBasePath)
        {
            try
            {
                lock (_lock)
                {
                    return _ocrEngine!.Detect(src);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "OCR detector failed once, reinitializing predictor and retrying.");
                ReinitializePredictor(modelBasePath);
                lock (_lock)
                {
                    return _ocrEngine!.Detect(src);
                }
            }
        }

        private static OcrRecognitionResult? TryRunRecognizerCandidate(string candidateName, Mat src, List<string> diagnostics)
        {
            try
            {
                lock (_lock)
                {
                    return _ocrEngine!.Recognize(src);
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add($"rec-candidate={candidateName}, failed={ex.GetType().Name}, message={ex.Message}");
                Log.Warning(ex, "OCR recognizer candidate failed. Name={Name}, Width={Width}, Height={Height}", candidateName, src.Width, src.Height);
                return null;
            }
        }

        private static void EnsureInitialized(string modelBasePath)
        {
            if (_ocrEngine != null && string.Equals(_initializedModelBasePath, modelBasePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_lock)
            {
                if (_ocrEngine != null && string.Equals(_initializedModelBasePath, modelBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _ocrEngine?.Dispose();
                _ocrEngine = null;
                _initializedModelBasePath = null;

                string detDir = Path.Combine(modelBasePath, "det");
                string recDir = Path.Combine(modelBasePath, "rec");
                Log.Information(
                    "Initializing PP-OCRv6 Small model. BasePath={BasePath}, DetectionPath={DetectionPath}, RecognitionPath={RecognitionPath}",
                    modelBasePath,
                    detDir,
                    recDir);

                var modelBundle = new PaddleOcrOnnxModelBundle(modelBasePath);
                _ocrEngine = new PaddleOcrOnnxEngine(modelBundle);
                _initializedModelBasePath = modelBasePath;
            }
        }

        private static void ReinitializePredictor(string modelBasePath)
        {
            lock (_lock)
            {
                _ocrEngine?.Dispose();
                _ocrEngine = null;
                _initializedModelBasePath = null;
                EnsureInitialized(modelBasePath);
            }
        }

        private static IEnumerable<(string Name, Mat Image)> CreateRecognitionCandidates(Mat src)
        {
            yield return ("original", src.Clone());

            using var upscaled = new Mat();
            Cv2.Resize(src, upscaled, new OpenCvSharp.Size(), 2.0d, 2.0d, InterpolationFlags.Cubic);
            yield return ("upscaled-2x", upscaled.Clone());

            using var gray = new Mat();
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);

            using var normalized = new Mat();
            Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);

            using var binary = new Mat();
            Cv2.Threshold(normalized, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            using var binaryBgr = new Mat();
            Cv2.CvtColor(binary, binaryBgr, ColorConversionCodes.GRAY2BGR);
            yield return ("otsu-binary", binaryBgr.Clone());

            using var adaptive = new Mat();
            Cv2.AdaptiveThreshold(normalized, adaptive, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 31, 11);
            using var adaptiveBgr = new Mat();
            Cv2.CvtColor(adaptive, adaptiveBgr, ColorConversionCodes.GRAY2BGR);
            yield return ("adaptive-binary", adaptiveBgr.Clone());

            using var brightBox = ExtractBrightTextBoxRegion(src);
            if (!brightBox.Empty())
            {
                yield return ("bright-box", brightBox.Clone());

                using var sharpenedBrightBox = SharpenAndBoostText(brightBox);
                if (!sharpenedBrightBox.Empty())
                {
                    yield return ("bright-box-sharpened", sharpenedBrightBox.Clone());
                }
            }

            using var focused = ExtractPrimaryTextRegion(src);
            if (!focused.Empty())
            {
                yield return ("primary-region", focused.Clone());
            }

            using var centerBand = ExtractCenterTextBand(src);
            if (!centerBand.Empty())
            {
                yield return ("center-band", centerBand.Clone());

                using var sharpenedCenterBand = SharpenAndBoostText(centerBand);
                if (!sharpenedCenterBand.Empty())
                {
                    yield return ("center-band-sharpened", sharpenedCenterBand.Clone());
                }
            }
        }

        private static Mat ExtractBrightTextBoxRegion(Mat src)
        {
            using var gray = new Mat();
            using var mask = new Mat();
            using var closed = new Mat();

            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, mask, 235, 255, ThresholdTypes.Binary);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(9, 5));
            Cv2.MorphologyEx(mask, closed, MorphTypes.Close, kernel);

            Cv2.FindContours(closed, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            OpenCvSharp.Rect bestRect = default;
            double bestScore = double.MinValue;
            bool hasBestRect = false;
            double centerX = src.Width / 2.0d;
            double centerY = src.Height / 2.0d;

            foreach (OpenCvSharp.Point[] contour in contours)
            {
                OpenCvSharp.Rect rect = Cv2.BoundingRect(contour);
                if (rect.Width < src.Width / 8 || rect.Height < 24)
                {
                    continue;
                }

                double aspectRatio = rect.Height == 0 ? 0 : (double)rect.Width / rect.Height;
                if (aspectRatio < 3.0d)
                {
                    continue;
                }

                double area = rect.Width * rect.Height;
                double rectCenterX = rect.X + rect.Width / 2.0d;
                double rectCenterY = rect.Y + rect.Height / 2.0d;
                double distance = Math.Abs(rectCenterX - centerX) + Math.Abs(rectCenterY - centerY);
                double normalizedDistance = distance / Math.Max(1.0d, src.Width + src.Height);
                double score = area * Math.Min(aspectRatio, 18.0d) - normalizedDistance * 60000.0d;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRect = rect;
                    hasBestRect = true;
                }
            }

            if (!hasBestRect)
            {
                return new Mat();
            }

            int padding = 16;
            int x = Math.Max(0, bestRect.X - padding);
            int y = Math.Max(0, bestRect.Y - padding);
            int width = Math.Min(src.Width - x, bestRect.Width + padding * 2);
            int height = Math.Min(src.Height - y, bestRect.Height + padding * 2);

            using var cropped = new Mat(src, new OpenCvSharp.Rect(x, y, width, height));
            var enlarged = new Mat();
            Cv2.Resize(cropped, enlarged, new OpenCvSharp.Size(), 3.0d, 3.0d, InterpolationFlags.Cubic);
            return enlarged;
        }

        private static Mat ExtractPrimaryTextRegion(Mat src)
        {
            var gray = new Mat();
            var thresh = new Mat();
            try
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.Threshold(gray, thresh, 210, 255, ThresholdTypes.Binary);

                Cv2.FindContours(thresh, out OpenCvSharp.Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                OpenCvSharp.Rect bestRect = default;
                double bestScore = double.MinValue;
                bool hasBestRect = false;
                double imageCenterX = src.Width / 2.0d;
                double imageCenterY = src.Height / 2.0d;

                foreach (OpenCvSharp.Point[] contour in contours)
                {
                    OpenCvSharp.Rect rect = Cv2.BoundingRect(contour);
                    if (rect.Width < src.Width / 6 || rect.Height < 18)
                    {
                        continue;
                    }

                    double aspectRatio = rect.Height == 0 ? 0 : (double)rect.Width / rect.Height;
                    if (aspectRatio < 4.0d)
                    {
                        continue;
                    }

                    double area = rect.Width * rect.Height;
                    double centerX = rect.X + rect.Width / 2.0d;
                    double centerY = rect.Y + rect.Height / 2.0d;
                    double centerDistance = Math.Abs(centerX - imageCenterX) + Math.Abs(centerY - imageCenterY);
                    double normalizedDistance = centerDistance / Math.Max(1.0d, src.Width + src.Height);
                    double score = area * Math.Min(aspectRatio, 20.0d) - normalizedDistance * 50000.0d;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRect = rect;
                        hasBestRect = true;
                    }
                }

                if (!hasBestRect)
                {
                    return new Mat();
                }

                int padding = 12;
                int x = Math.Max(0, bestRect.X - padding);
                int y = Math.Max(0, bestRect.Y - padding);
                int width = Math.Min(src.Width - x, bestRect.Width + padding * 2);
                int height = Math.Min(src.Height - y, bestRect.Height + padding * 2);
                var cropped = new Mat(src, new OpenCvSharp.Rect(x, y, width, height));

                var enlarged = new Mat();
                Cv2.Resize(cropped, enlarged, new OpenCvSharp.Size(), 2.5d, 2.5d, InterpolationFlags.Cubic);
                return enlarged;
            }
            finally
            {
                gray.Dispose();
                thresh.Dispose();
            }
        }

        private static Mat ExtractCenterTextBand(Mat src)
        {
            int bandWidth = (int)(src.Width * 0.75d);
            int bandHeight = Math.Min(160, Math.Max(40, src.Height / 5));
            int x = Math.Max(0, (src.Width - bandWidth) / 2);
            int y = Math.Max(0, (src.Height - bandHeight) / 2);

            if (bandWidth <= 0 || bandHeight <= 0 || x + bandWidth > src.Width || y + bandHeight > src.Height)
            {
                return new Mat();
            }

            using var cropped = new Mat(src, new OpenCvSharp.Rect(x, y, bandWidth, bandHeight));
            var enlarged = new Mat();
            Cv2.Resize(cropped, enlarged, new OpenCvSharp.Size(), 3.0d, 3.0d, InterpolationFlags.Cubic);
            return enlarged;
        }

        private static Mat SharpenAndBoostText(Mat src)
        {
            using var gray = new Mat();
            using var enlarged = new Mat();
            using var blurred = new Mat();
            using var sharpened = new Mat();
            using var binary = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));

            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Resize(gray, enlarged, new OpenCvSharp.Size(), 2.0d, 2.0d, InterpolationFlags.Lanczos4);
            Cv2.GaussianBlur(enlarged, blurred, new OpenCvSharp.Size(0, 0), 1.2d);
            Cv2.AddWeighted(enlarged, 1.8d, blurred, -0.8d, 0, sharpened);
            Cv2.Threshold(sharpened, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel);

            var output = new Mat();
            Cv2.CvtColor(binary, output, ColorConversionCodes.GRAY2BGR);
            return output;
        }

        private static void PersistDiagnostics(string sessionId, List<string> diagnostics, Mat originalImage, string ocrDebugPath)
        {
            try
            {
                TextLogCleanupHelper.CleanDirectories(ocrDebugPath, retentionDays: 7, retainedDirectoryCount: 20);
                Directory.CreateDirectory(ocrDebugPath);
                string sessionDir = Path.Combine(ocrDebugPath, sessionId);
                Directory.CreateDirectory(sessionDir);

                string logPath = Path.Combine(sessionDir, "ocr-diagnostics.txt");
                File.WriteAllText(logPath, string.Join(Environment.NewLine, diagnostics), Encoding.UTF8);

                string imagePath = Path.Combine(sessionDir, "original.png");
                Cv2.ImWrite(imagePath, originalImage);

                Log.Warning("OCR diagnostics saved to {Path}", sessionDir);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to persist OCR diagnostics.");
            }
        }

        private static bool ShouldPersistSuccessfulDiagnostics()
        {
            string? value = Environment.GetEnvironmentVariable("EXPORT_DOC_MANAGER_OCR_DEBUG");
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeLogText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
            return normalized.Length <= 160 ? normalized : normalized[..160];
        }

        private static float NormalizeRecognizerScore(float score)
        {
            return float.IsNaN(score) || float.IsInfinity(score) ? 0f : score;
        }

        private static int ScoreRecognizedText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            string normalized = text.Trim();
            int chineseCount = normalized.Count(ch => ch >= 0x4E00 && ch <= 0x9FFF);
            int alphaNumericCount = normalized.Count(char.IsLetterOrDigit);
            int punctuationCount = normalized.Count(ch => char.IsPunctuation(ch) || char.IsSymbol(ch));
            int whitespaceCount = normalized.Count(char.IsWhiteSpace);

            return normalized.Length * 4
                + chineseCount * 10
                + alphaNumericCount * 3
                + Math.Min(whitespaceCount, 4) * 2
                - punctuationCount * 2;
        }

    }
}

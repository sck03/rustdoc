using System;
using System.Collections.Generic;
using System.Linq;
using ExportDocManager.Utils;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ExportDocManager.Services.Reporting
{
    public class PdfMergeService : IPdfMergeService
    {
        internal const int MaxPagesPerFile = 500;
        internal const int MaxTotalPages = 1000;
        internal const long MaxTotalInputBytes = 250L * 1024L * 1024L;
        internal const double MaxPageDimensionPoints = 14_400d;
        internal const double MaxPageAreaPoints = 20_000_000d;
        internal const double MaxTotalPageAreaPoints = 500_000_000d;

        public void Merge(
            IReadOnlyCollection<string> sourceFiles,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(sourceFiles);
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            var files = sourceFiles
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .ToList();

            if (files.Count == 0)
            {
                throw new ArgumentException("至少需要一个 PDF 文件。", nameof(sourceFiles));
            }

            long totalInputBytes = 0;
            foreach (string file in files)
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    throw new FileNotFoundException("找不到待合并的 PDF 文件。", info.FullName);
                }
                totalInputBytes = checked(totalInputBytes + info.Length);
                if (totalInputBytes > MaxTotalInputBytes)
                {
                    throw new InvalidDataException("待合并 PDF 总大小超过 250 MB 限制。");
                }
            }

            AtomicFileHelper.WriteFileAtomic(
                destinationPath,
                (tempPath, token) => MergeInto(files, tempPath, token),
                cancellationToken);
        }

        private static void MergeInto(
            IReadOnlyCollection<string> sourceFiles,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            using var outputDocument = new PdfDocument();
            int totalPages = 0;
            double totalPageArea = 0;

            foreach (string file in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var inputDocument = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                int pageCount = inputDocument.PageCount;
                if (pageCount > MaxPagesPerFile)
                {
                    throw new InvalidDataException($"PDF 文件页数超过 {MaxPagesPerFile} 页限制：{Path.GetFileName(file)}。");
                }

                totalPages += pageCount;
                if (totalPages > MaxTotalPages)
                {
                    throw new InvalidDataException($"合并后的 PDF 总页数超过 {MaxTotalPages} 页限制。");
                }

                for (int index = 0; index < pageCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PdfPage page = inputDocument.Pages[index];
                    double width = page.Width.Point;
                    double height = page.Height.Point;
                    double area = width * height;
                    if (!double.IsFinite(width) || !double.IsFinite(height) ||
                        width <= 0 || height <= 0 ||
                        width > MaxPageDimensionPoints || height > MaxPageDimensionPoints ||
                        area > MaxPageAreaPoints)
                    {
                        throw new InvalidDataException($"PDF 页面尺寸超过安全限制：{Path.GetFileName(file)} 第 {index + 1} 页。");
                    }

                    totalPageArea += area;
                    if (!double.IsFinite(totalPageArea) || totalPageArea > MaxTotalPageAreaPoints)
                    {
                        throw new InvalidDataException("合并后的 PDF 页面总面积超过安全限制，请分批处理。");
                    }

                    outputDocument.AddPage(page);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            outputDocument.Save(destinationPath);
        }
    }
}

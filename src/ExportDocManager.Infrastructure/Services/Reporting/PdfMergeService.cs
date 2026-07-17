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

            foreach (string file in sourceFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var inputDocument = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                int pageCount = inputDocument.PageCount;
                for (int index = 0; index < pageCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outputDocument.AddPage(inputDocument.Pages[index]);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            outputDocument.Save(destinationPath);
        }
    }
}

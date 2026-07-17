namespace ExportDocManager.Utils
{
    public static class BatchExportPathHelper
    {
        public const string DefaultFileNamePattern = "{InvoiceNo}_{DocType}";
        public const string DefaultFolderPattern = "{InvoiceNo}_Docs_{Date}";

        public static string BuildBatchDirectory(
            string outputDirectory,
            string folderPattern,
            string invoiceNo,
            string customerName,
            DateTime exportDate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

            string safeInvoiceNo = SanitizePart(invoiceNo);
            string safeCustomer = SanitizePart(customerName);
            string dateText = exportDate.ToString("yyyyMMdd");
            string batchFolderName = ApplyPattern(
                NormalizePattern(folderPattern, DefaultFolderPattern),
                safeInvoiceNo,
                safeCustomer,
                string.Empty,
                dateText);

            if (string.IsNullOrWhiteSpace(batchFolderName))
            {
                batchFolderName = $"{safeInvoiceNo}_Docs_{dateText}";
            }

            return Path.Combine(outputDirectory, batchFolderName);
        }

        public static string BuildDocumentFileName(
            string directory,
            string fileNamePattern,
            string invoiceNo,
            string customerName,
            string documentName,
            DateTime exportDate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);

            string dateText = exportDate.ToString("yyyyMMdd");
            string fileNameWithoutExtension = ApplyPattern(
                NormalizePattern(fileNamePattern, DefaultFileNamePattern),
                SanitizePart(invoiceNo),
                SanitizePart(customerName),
                SanitizePart(documentName),
                dateText);

            if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            {
                fileNameWithoutExtension = $"{SanitizePart(invoiceNo)}_{SanitizePart(documentName)}";
            }

            return EnsureUniqueFileName(directory, $"{fileNameWithoutExtension}.pdf");
        }

        public static string BuildArchiveFileName(
            string directory,
            string invoiceNo,
            string customerName,
            DateTime exportDate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);

            string archiveName = ApplyPattern(
                DefaultFolderPattern,
                SanitizePart(invoiceNo),
                SanitizePart(customerName),
                "Docs",
                exportDate.ToString("yyyyMMdd"));

            return EnsureUniqueFileName(directory, $"{archiveName}.zip");
        }

        public static string BuildMergedPdfFileName(
            string directory,
            string invoiceNo,
            string customerName,
            DateTime exportDate)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);

            string mergedName = ApplyPattern(
                "{InvoiceNo}_AllDocs_{Date}",
                SanitizePart(invoiceNo),
                SanitizePart(customerName),
                "AllDocs",
                exportDate.ToString("yyyyMMdd"));

            return EnsureUniqueFileName(directory, $"{mergedName}.pdf");
        }

        public static string SanitizePart(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            return string
                .Join("_", input.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
                .Trim('_');
        }

        private static string ApplyPattern(
            string pattern,
            string invoiceNo,
            string customer,
            string docType,
            string date)
        {
            var output = pattern ?? string.Empty;
            output = output.Replace("{InvoiceNo}", invoiceNo ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            output = output.Replace("{Customer}", customer ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            output = output.Replace("{DocType}", docType ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            output = output.Replace("{Date}", date ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            return SanitizePart(output);
        }

        private static string NormalizePattern(string pattern, string fallback)
        {
            var trimmed = pattern?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
        }

        private static string EnsureUniqueFileName(string directory, string fileName)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var candidate = fileName;
            var index = 1;
            while (File.Exists(Path.Combine(directory, candidate)))
            {
                candidate = $"{fileNameWithoutExtension}_{index}{extension}";
                index++;
            }

            return candidate;
        }
    }
}

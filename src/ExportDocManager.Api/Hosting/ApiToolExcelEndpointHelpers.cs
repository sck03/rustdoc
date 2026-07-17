namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        internal static IResult ValidateExcelSourcePath(
            string inputPath,
            string fieldName,
            out string sourcePath)
        {
            sourcePath = string.Empty;
            string candidate = inputPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}不能为空。"));
            }

            if (!IsSupportedExcelSourceExtension(candidate))
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}必须是 Excel 文件（.xlsx、.xlsm、.xltx、.xltm 或 .xls）。"));
            }

            try
            {
                sourcePath = Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}无效：{ex.Message}"));
            }

            return File.Exists(sourcePath)
                ? null
                : Results.BadRequest(new ApiErrorResponse($"{fieldName}不存在：{sourcePath}"));
        }

        internal static IResult ValidateExcelDestinationPath(
            string inputPath,
            string fieldName,
            out string destinationPath)
        {
            destinationPath = string.Empty;
            string candidate = inputPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}不能为空。"));
            }

            if (!string.Equals(Path.GetExtension(candidate), ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}必须以 .xlsx 结尾。"));
            }

            try
            {
                destinationPath = Path.GetFullPath(candidate);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return Results.BadRequest(new ApiErrorResponse($"{fieldName}无效：{ex.Message}"));
            }
        }

        private static bool IsSupportedExcelSourceExtension(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xltx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xltm", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase);
        }
    }
}

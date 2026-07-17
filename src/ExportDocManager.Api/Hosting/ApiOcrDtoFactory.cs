using ExportDocManager.Services.Tools;

namespace ExportDocManager.Api.Hosting
{
    internal static class ApiOcrDtoFactory
    {
        public const string FilePathStoragePolicy =
            "智能 OCR 只读取用户显式选择或输入的图片路径，识别文本随响应返回；不会复制源图片、写入默认输出目录或创建系统 C 盘落点。OCR 模型仍随程序放在 OcrModels/ 下。";

        public const string InMemoryStoragePolicy =
            "智能 OCR 剪贴板等无路径图片只通过请求体内存传递，识别文本随响应返回；不会把图片落地为临时文件、写入数据库、创建默认输出目录或创建系统 C 盘落点。OCR 模型仍随程序放在 OcrModels/ 下。";

        public static ApiOcrRecognizeImageResponse FromResult(
            OcrResult result,
            string sourcePath,
            string storagePolicy)
        {
            return new ApiOcrRecognizeImageResponse
            {
                SourcePath = sourcePath,
                FullText = result?.FullText ?? string.Empty,
                Lines = (result?.Lines ?? [])
                    .Where(line => line != null)
                    .Select(line => new ApiOcrLineDto
                    {
                        Text = line.Text ?? string.Empty,
                        X = line.X,
                        Y = line.Y,
                        Width = line.Width,
                        Height = line.Height
                    })
                    .ToList(),
                StoragePolicy = storagePolicy
            };
        }
    }
}

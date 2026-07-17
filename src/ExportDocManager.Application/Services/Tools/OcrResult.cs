namespace ExportDocManager.Services.Tools
{
    public class OcrResult
    {
        public string FullText { get; set; }
        public List<OcrLine> Lines { get; set; } = new List<OcrLine>();
    }

    public class OcrLine
    {
        public string Text { get; set; }
        // 简单的边界框: X, Y, Width, Height
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}

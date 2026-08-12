internal sealed record GeneratorOptions(string OutputPath, string BaseUrl)
{
    public static GeneratorOptions Parse(string[] args)
    {
        string outputPath = Path.Combine(
            "apps",
            "export-doc-web",
            "src",
            "api",
            "generated",
            "exportDocManagerApi.ts");
        string baseUrl = "http://127.0.0.1:5188";

        for (int index = 0; index < args.Length; index++)
        {
            string arg = args[index];
            if (string.Equals(arg, "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                outputPath = args[++index];
                continue;
            }

            if (string.Equals(arg, "--base-url", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                baseUrl = args[++index];
                continue;
            }

            throw new ArgumentException($"Unknown or incomplete argument: {arg}");
        }

        return new GeneratorOptions(Path.GetFullPath(outputPath), baseUrl);
    }
}

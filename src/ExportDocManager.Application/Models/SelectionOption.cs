namespace ExportDocManager.ViewModels
{
    public sealed class SelectionOption<T>
    {
        public SelectionOption(T value, string text)
        {
            Value = value;
            Text = text ?? string.Empty;
        }

        public T Value { get; }

        public string Text { get; }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Text))
            {
                return Text;
            }

            return Value?.ToString() ?? string.Empty;
        }
    }
}

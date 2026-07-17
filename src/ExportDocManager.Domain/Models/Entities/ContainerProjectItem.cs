namespace ExportDocManager.Models.Entities
{
    public class ContainerProjectItem
    {
        public int Id { get; set; }
        public int ProjectId { get; set; }
        public string Name { get; set; }
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal Weight { get; set; }
        public int Quantity { get; set; }
        public bool UsePallet { get; set; }
        public int UnitsPerPallet { get; set; } = 1;
        public decimal MaxTopLoadWeight { get; set; }
        public string PreferredZone { get; set; } = string.Empty;
        public int LoadSequence { get; set; } = 1;
        public string PriorityGroup { get; set; } = string.Empty;
        public int ColorArgb { get; set; }
    }
}

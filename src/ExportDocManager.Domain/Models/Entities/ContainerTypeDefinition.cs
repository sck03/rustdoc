namespace ExportDocManager.Models.Entities
{
    public class ContainerTypeDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; } // e.g., "Maersk 20GP", "20GP"
        public int Length { get; set; } // cm
        public int Width { get; set; }  // cm
        public int Height { get; set; } // cm
        public decimal MaxVolume { get; set; } // CBM
        public decimal MaxWeight { get; set; } // KGS
        public bool IsSystemDefault { get; set; } // true for built-in types
    }
}

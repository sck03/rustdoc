namespace ExportDocManager.Models.Entities
{
    public class Payee
    {
        public int Id { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string BankName { get; set; }
        public string RMBAccount { get; set; }
        public string USDAccount { get; set; }
        public string ContactPerson { get; set; }
        public string Phone { get; set; }
        public string Notes { get; set; }
    }
}


using System;
using System.Collections.Generic;
namespace ExportDocManager.Models.Entities
{
    public class ContainerProject
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ContainerType { get; set; } // 20GP, 40GP, etc.
        
        // Custom Container Dimensions
        public int ContainerLength { get; set; } // cm
        public int ContainerWidth { get; set; }  // cm
        public int ContainerHeight { get; set; } // cm
        public decimal ContainerMaxVolume { get; set; } // CBM
        public decimal ContainerMaxWeight { get; set; } // KGS
        public bool AllowRotation { get; set; } = true;
        public bool UsePalletConstraints { get; set; }
        public int DefaultPalletLength { get; set; } = 120;
        public int DefaultPalletWidth { get; set; } = 100;
        public int DefaultPalletHeight { get; set; } = 15;
        public decimal DefaultPalletWeight { get; set; } = 25m;
        public bool EnforceCenterOfGravity { get; set; } = true;
        public decimal CenterOfGravityTolerancePercent { get; set; } = 20m;
        public decimal MinimumSupportAreaPercent { get; set; } = 100m;
        public bool RequireSameFootprintStacking { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<ContainerProjectItem> Items { get; set; } = new();
    }
}

using System;
using System.ComponentModel.DataAnnotations;

namespace ExportDocManager.Models.Entities
{
    /// <summary>
    /// Represents a product in the product library (master data).
    /// 代表商品库中的基础商品信息。
    /// </summary>
    public class Product
    {
        public int Id { get; set; }

        [Display(Name = "货号")]
        public string ProductCode { get; set; } // SKU or Style No

        [Display(Name = "英文品名")]
        public string NameEN { get; set; }

        [Display(Name = "中文品名")]
        public string NameCN { get; set; }

        [Display(Name = "描述")]
        public string Description { get; set; }

        [Display(Name = "HS编码")]
        public string HSCode { get; set; }

        [Display(Name = "申报要素")]
        public string Elements { get; set; } // Declaration Elements

        [Display(Name = "监管条件")]
        public string SupervisionConditions { get; set; } // Supervision Conditions

        [Display(Name = "检验检疫")]
        public string InspectionCategory { get; set; } // Inspection Category

        [Display(Name = "退税率(%)")]
        public decimal TaxRebateRate { get; set; }

        [Display(Name = "材质/成分")]
        public string Material { get; set; } // FabricComposition

        [Display(Name = "品牌")]
        public string Brand { get; set; }

        [Display(Name = "原产地")]
        public string Origin { get; set; }

        [Display(Name = "单位(英)")]
        public string UnitEN { get; set; }

        [Display(Name = "单位(中)")]
        public string UnitCN { get; set; }

        // Packing Details
        public decimal Length { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        
        [Display(Name = "单箱毛重")]
        public decimal GWPerCtn { get; set; }
        
        [Display(Name = "单箱净重")]
        public decimal NWPerCtn { get; set; }

        [Display(Name = "每箱数量")]
        public decimal PcsPerCtn { get; set; } // Added for Smart Packing

        [Display(Name = "包装单位(英)")]
        public string PackageUnitEN { get; set; }

        [Display(Name = "包装单位(中)")]
        public string PackageUnitCN { get; set; }

        [Display(Name = "默认单价")]
        public decimal DefaultPrice { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        [ConcurrencyCheck]
        public byte[] RowVersion { get; set; }
    }
}

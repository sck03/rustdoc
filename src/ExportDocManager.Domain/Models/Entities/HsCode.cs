using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;
using ExportDocManager.Utils;

namespace ExportDocManager.Models.Entities
{
    [Table("HsCodes")]
    public class HsCode : INotifyPropertyChanged
    {
        private int _id;
        private string _code = string.Empty;
        private string _normalizedCode = string.Empty;
        private string _name = string.Empty;
        private string _unit;
        private string _description;
        private string _elements;
        private string _supervisionConditions;
        private string _inspectionCategory;
        private string _rebateRate;
        private DateTime? _updateTime = DateTime.Now;
        private string _detailUrl;
        private string _status = "Active";
        private string _sourceName;
        private int? _effectiveYear;
        private DateTime? _lastVerifiedAt;
        private string _replacedByCodes;
        private string _normalTariffRate;
        private string _preferentialTariffRate;
        private string _exportTariffRate;
        private string _consumptionTaxRate;
        private string _valueAddedTaxRate;
        private string _notes;

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        [Required]
        [MaxLength(20)]
        public string Code
        {
            get => _code;
            set
            {
                value ??= string.Empty;
                string normalizedCode = HsCodeTextHelper.NormalizeCode(value);
                bool codeChanged = !string.Equals(_code, value, StringComparison.Ordinal);
                bool normalizedChanged = !string.Equals(_normalizedCode, normalizedCode, StringComparison.Ordinal);
                if (!codeChanged && !normalizedChanged)
                {
                    return;
                }

                _code = value;
                _normalizedCode = normalizedCode;

                if (codeChanged)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
                }

                if (normalizedChanged)
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NormalizedCode)));
                }
            }
        } // HS编码

        [MaxLength(20)]
        public string NormalizedCode
        {
            get => string.IsNullOrWhiteSpace(_normalizedCode)
                ? HsCodeTextHelper.NormalizeCode(_code)
                : _normalizedCode;
            private set => SetProperty(ref _normalizedCode, string.IsNullOrWhiteSpace(value)
                ? HsCodeTextHelper.NormalizeCode(_code)
                : value);
        }

        [Required]
        [MaxLength(200)]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        } // 商品名称

        [MaxLength(50)]
        public string Unit
        {
            get => _unit;
            set => SetProperty(ref _unit, value);
        } // 法定单位

        [MaxLength(500)]
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        } // 描述

        [MaxLength(500)]
        public string Elements
        {
            get => _elements;
            set => SetProperty(ref _elements, value);
        } // 申报要素

        [MaxLength(50)]
        public string SupervisionConditions
        {
            get => _supervisionConditions;
            set => SetProperty(ref _supervisionConditions, value);
        } // 监管条件 (e.g., A/B)

        [MaxLength(50)]
        public string InspectionCategory
        {
            get => _inspectionCategory;
            set => SetProperty(ref _inspectionCategory, value);
        } // 检验检疫类别 (e.g., M/N)

        [MaxLength(50)]
        public string RebateRate
        {
            get => _rebateRate;
            set => SetProperty(ref _rebateRate, value);
        } // 出口退税率

        public DateTime? UpdateTime
        {
            get => _updateTime;
            set => SetProperty(ref _updateTime, value);
        }

        [MaxLength(30)]
        public string Status
        {
            get => string.IsNullOrWhiteSpace(_status) ? "Active" : _status;
            set => SetProperty(ref _status, string.IsNullOrWhiteSpace(value) ? "Active" : value.Trim());
        }

        [MaxLength(200)]
        public string SourceName
        {
            get => _sourceName;
            set => SetProperty(ref _sourceName, value?.Trim());
        }

        public int? EffectiveYear
        {
            get => _effectiveYear;
            set => SetProperty(ref _effectiveYear, value);
        }

        public DateTime? LastVerifiedAt
        {
            get => _lastVerifiedAt;
            set => SetProperty(ref _lastVerifiedAt, value);
        }

        [MaxLength(500)]
        public string ReplacedByCodes
        {
            get => _replacedByCodes;
            set => SetProperty(ref _replacedByCodes, value?.Trim());
        }

        [MaxLength(50)]
        public string NormalTariffRate
        {
            get => _normalTariffRate;
            set => SetProperty(ref _normalTariffRate, value?.Trim());
        }

        [MaxLength(50)]
        public string PreferentialTariffRate
        {
            get => _preferentialTariffRate;
            set => SetProperty(ref _preferentialTariffRate, value?.Trim());
        }

        [MaxLength(50)]
        public string ExportTariffRate
        {
            get => _exportTariffRate;
            set => SetProperty(ref _exportTariffRate, value?.Trim());
        }

        [MaxLength(50)]
        public string ConsumptionTaxRate
        {
            get => _consumptionTaxRate;
            set => SetProperty(ref _consumptionTaxRate, value?.Trim());
        }

        [MaxLength(50)]
        public string ValueAddedTaxRate
        {
            get => _valueAddedTaxRate;
            set => SetProperty(ref _valueAddedTaxRate, value?.Trim());
        }

        [MaxLength(1000)]
        public string Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value?.Trim());
        }

        [NotMapped]
        public string DetailUrl
        {
            get => _detailUrl;
            set => SetProperty(ref _detailUrl, value);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

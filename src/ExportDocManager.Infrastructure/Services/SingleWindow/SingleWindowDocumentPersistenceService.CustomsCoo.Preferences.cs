using ExportDocManager.Models;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Models.SingleWindow;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed partial class SingleWindowDocumentPersistenceService
    {
        private void ApplyStoredCustomsCooDefaults(CooMappedDocument document)
        {
            if (document == null || _settingsService.Settings?.SingleWindow?.CustomsCooDefaults == null)
            {
                return;
            }

            CustomsCooDefaultProfileApplicator.Apply(
                document,
                _settingsService.Settings.SingleWindow.CustomsCooDefaults);
        }

        private async Task RememberCustomsCooDefaultsAsync(CustomsCooDocument document)
        {
            if (document == null)
            {
                return;
            }

            await _settingsService.UpdateAsync(settings =>
            {
                settings.SingleWindow ??= new SingleWindowSettings();
                settings.SingleWindow.CustomsCooDefaults ??= new CustomsCooDefaultProfile();
                var defaults = settings.SingleWindow.CustomsCooDefaults;

                bool changed = false;
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.ApplName), document.ApplName);
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.Applicant), document.Applicant);
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.ApplTel), document.ApplTel);
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.OrgCode), document.OrgCode);
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.FetchPlace), document.FetchPlace);
                changed |= UpdateRememberedDefault(defaults, nameof(defaults.AplAdd), document.AplAdd);
                return changed;
            }).ConfigureAwait(false);
        }

        private async Task RememberCustomsCooProducerProfilesAsync(
            CustomsCooDocument document,
            string sourceInvoiceNo,
            string sourceContractNo,
            CancellationToken cancellationToken)
        {
            if (document == null)
            {
                return;
            }

            var inputs = (document.Items ?? [])
                .Where(item => item != null)
                .Select(item => new CustomsCooProducerProfileInput
                {
                    CiqRegNo = item.CiqRegNo,
                    PrdcEtpsName = item.PrdcEtpsName,
                    PrdcEtpsConcEr = item.PrdcEtpsConcEr,
                    PrdcEtpsTel = item.PrdcEtpsTel,
                    Producer = item.Producer,
                    ProducerTel = item.ProducerTel,
                    ProducerFax = item.ProducerFax,
                    ProducerEmail = item.ProducerEmail,
                    ProducerSertFlag = item.ProducerSertFlag,
                    LastInvoiceNo = string.IsNullOrWhiteSpace(document.InvoiceNo) ? sourceInvoiceNo : document.InvoiceNo,
                    LastContractNo = string.IsNullOrWhiteSpace(document.ContractNo) ? sourceContractNo : document.ContractNo,
                    LastSourceStyleNo = item.SourceStyleNo
                })
                .ToList();

            if (inputs.Count == 0)
            {
                return;
            }

            await _producerProfileService.RememberProfilesAsync(inputs, cancellationToken).ConfigureAwait(false);
        }

        private static bool UpdateRememberedDefault(CustomsCooDefaultProfile defaults, string fieldName, string source)
        {
            string normalized = NormalizeStoredDefault(source);
            if (defaults == null || string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return fieldName switch
            {
                nameof(CustomsCooDefaultProfile.ApplName) => SetRememberedDefaultValue(defaults.ApplName, normalized, value => defaults.ApplName = value),
                nameof(CustomsCooDefaultProfile.Applicant) => SetRememberedDefaultValue(defaults.Applicant, normalized, value => defaults.Applicant = value),
                nameof(CustomsCooDefaultProfile.ApplTel) => SetRememberedDefaultValue(defaults.ApplTel, normalized, value => defaults.ApplTel = value),
                nameof(CustomsCooDefaultProfile.OrgCode) => SetRememberedDefaultValue(defaults.OrgCode, normalized, value => defaults.OrgCode = value),
                nameof(CustomsCooDefaultProfile.FetchPlace) => SetRememberedDefaultValue(defaults.FetchPlace, normalized, value => defaults.FetchPlace = value),
                nameof(CustomsCooDefaultProfile.AplAdd) => SetRememberedDefaultValue(defaults.AplAdd, normalized, value => defaults.AplAdd = value),
                _ => false
            };
        }

        private static bool SetRememberedDefaultValue(string currentValue, string newValue, Action<string> setter)
        {
            if (setter == null || string.IsNullOrWhiteSpace(newValue))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(NormalizeStoredDefault(currentValue)))
            {
                return false;
            }

            setter(newValue);
            return true;
        }

        private static string NormalizeStoredDefault(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}

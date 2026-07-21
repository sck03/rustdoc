using ExportDocManager.DataAccess;
using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public partial class HsCodeService
    {
        public async Task SaveAsync(HsCode hsCode)
        {
            if (hsCode == null)
            {
                return;
            }

            string normalizedCode = HsCodeTextHelper.NormalizeCode(hsCode.Code);
            if (string.IsNullOrWhiteSpace(normalizedCode))
            {
                throw new InvalidOperationException("HS 编码不能为空。");
            }

            hsCode.Code = normalizedCode;
            hsCode.Status = NormalizeHsCodeStatus(hsCode.Status);

            await using var context = await CreateDbContextAsync();
            var existing = await context.HsCodes.FirstOrDefaultAsync(h => h.NormalizedCode == normalizedCode);
            if (string.Equals(hsCode.Status, HsCodeValidityPolicy.ActiveStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (existing == null)
                {
                    throw new InvalidOperationException("手工新建的 HS 编码只能保存为“仅供参考”；当前有效编码请通过年度税则导入建立。");
                }

                if (!string.Equals(existing.Status, HsCodeValidityPolicy.ActiveStatus, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("不能通过普通编辑把参考或作废编码改为当前有效；请使用年度税则导入或知识库迁移。");
                }

                hsCode.SourceName = string.IsNullOrWhiteSpace(hsCode.SourceName) ? existing.SourceName : hsCode.SourceName;
                hsCode.EffectiveYear ??= existing.EffectiveYear;
                hsCode.LastVerifiedAt ??= existing.LastVerifiedAt;
                HsCodeValidityPolicy.EnsureTrustedActiveMetadata(hsCode);
            }
            if (existing != null)
            {
                if (string.Equals(existing.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(hsCode.Status, "ReferenceOnly", StringComparison.OrdinalIgnoreCase))
                {
                    hsCode.Id = existing.Id;
                    return;
                }
                CopyHsCodeValues(hsCode, existing);
                existing.UpdateTime = DateTime.Now;
                context.HsCodes.Update(existing);
                hsCode.Id = existing.Id;
            }
            else
            {
                hsCode.Id = 0;
                hsCode.UpdateTime = DateTime.Now;
                await context.HsCodes.AddAsync(hsCode);
            }

            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            if (id <= 0)
            {
                return;
            }

            await using var context = await CreateDbContextAsync();
            var item = await context.HsCodes.FindAsync(id);
            if (item == null)
            {
                return;
            }

            context.HsCodes.Remove(item);
            await context.SaveChangesAsync();
        }

        public async Task DeleteAsync(IEnumerable<int> ids)
        {
            if (ids == null)
            {
                return;
            }

            var validIds = ids
                .Where(id => id > 0)
                .Distinct()
                .ToList();
            if (validIds.Count == 0)
            {
                return;
            }

            await using var context = await CreateDbContextAsync();
            var items = await context.HsCodes
                .Where(h => validIds.Contains(h.Id))
                .ToListAsync();
            if (items.Count == 0)
            {
                return;
            }

            context.HsCodes.RemoveRange(items);
            await context.SaveChangesAsync();
        }

        public async Task<List<HsCode>> GetAllLocalAsync()
        {
            var rows = await GetReadRepository().QueryAsync(new HsCodeReadQuery
            {
                ReturnAll = true,
                PageSize = 100
            });
            return rows.ToList();
        }

        public async Task<PagedResult<HsCode>> GetPagedLocalAsync(int pageNumber, int pageSize, string keyword = null)
        {
            return await GetReadRepository().QueryPageAsync(new HsCodeReadQuery
            {
                Keyword = keyword ?? string.Empty,
                PageNumber = pageNumber,
                PageSize = pageSize
            });
        }

        public async Task ClearAllLocalAsync()
        {
            await using var context = await CreateDbContextAsync();
            context.HsCodes.RemoveRange(context.HsCodes);
            await context.SaveChangesAsync();
        }

        private static void CopyHsCodeValues(HsCode source, HsCode target)
        {
            target.Code = source.Code;
            target.Name = source.Name;
            target.Unit = source.Unit;
            target.RebateRate = source.RebateRate;
            if (!string.IsNullOrWhiteSpace(source.NormalTariffRate)) target.NormalTariffRate = source.NormalTariffRate;
            if (!string.IsNullOrWhiteSpace(source.PreferentialTariffRate)) target.PreferentialTariffRate = source.PreferentialTariffRate;
            if (!string.IsNullOrWhiteSpace(source.ExportTariffRate)) target.ExportTariffRate = source.ExportTariffRate;
            if (!string.IsNullOrWhiteSpace(source.ConsumptionTaxRate)) target.ConsumptionTaxRate = source.ConsumptionTaxRate;
            if (!string.IsNullOrWhiteSpace(source.ValueAddedTaxRate)) target.ValueAddedTaxRate = source.ValueAddedTaxRate;
            if (!string.IsNullOrWhiteSpace(source.Notes)) target.Notes = source.Notes;
            target.SupervisionConditions = source.SupervisionConditions;
            target.InspectionCategory = source.InspectionCategory;
            target.Elements = source.Elements;
            target.Description = source.Description;
            target.DetailUrl = source.DetailUrl;
            target.Status = NormalizeHsCodeStatus(source.Status);
            if (!string.IsNullOrWhiteSpace(source.SourceName)) target.SourceName = source.SourceName;
            if (source.EffectiveYear.HasValue) target.EffectiveYear = source.EffectiveYear;
            if (source.LastVerifiedAt.HasValue) target.LastVerifiedAt = source.LastVerifiedAt;
            if (!string.IsNullOrWhiteSpace(source.ReplacedByCodes)) target.ReplacedByCodes = source.ReplacedByCodes;
        }

        private static string NormalizeHsCodeStatus(string status)
        {
            string value = (status ?? string.Empty).Trim();
            return value.ToUpperInvariant() switch
            {
                "SUSPECTEDOBSOLETE" or "疑似作废" => "SuspectedObsolete",
                "OBSOLETE" or "已作废" or "作废" => "Obsolete",
                "REFERENCEONLY" or "仅供参考" or "参考" => "ReferenceOnly",
                "ACTIVE" or "有效" => "Active",
                _ => "ReferenceOnly"
            };
        }
    }
}

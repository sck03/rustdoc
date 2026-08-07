using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        private static void MergeHsCode(HsCode source, HsCode target)
        {
            target.Name = Prefer(source.Name, target.Name); target.Unit = Prefer(source.Unit, target.Unit);
            target.Description = Prefer(source.Description, target.Description); target.Elements = Prefer(source.Elements, target.Elements);
            target.SupervisionConditions = Prefer(source.SupervisionConditions, target.SupervisionConditions);
            target.InspectionCategory = Prefer(source.InspectionCategory, target.InspectionCategory); target.RebateRate = Prefer(source.RebateRate, target.RebateRate);
            target.NormalTariffRate = Prefer(source.NormalTariffRate, target.NormalTariffRate); target.PreferentialTariffRate = Prefer(source.PreferentialTariffRate, target.PreferentialTariffRate);
            target.ExportTariffRate = Prefer(source.ExportTariffRate, target.ExportTariffRate); target.ConsumptionTaxRate = Prefer(source.ConsumptionTaxRate, target.ConsumptionTaxRate);
            target.ValueAddedTaxRate = Prefer(source.ValueAddedTaxRate, target.ValueAddedTaxRate); target.Notes = Prefer(source.Notes, target.Notes);
            target.SourceName = Prefer(source.SourceName, target.SourceName); target.EffectiveYear = Max(source.EffectiveYear, target.EffectiveYear);
            target.LastVerifiedAt = Max(source.LastVerifiedAt, target.LastVerifiedAt); target.UpdateTime = Max(source.UpdateTime, target.UpdateTime);
            if (HsCodeValidityPolicy.IsTrustedActive(source)) target.Status = "Active";
        }

        private static void MergeExample(HsCodeDeclarationExample source, HsCodeDeclarationExample target)
        {
            target.ResolvedCurrentHsCode = Prefer(source.ResolvedCurrentHsCode, target.ResolvedCurrentHsCode);
            target.ProductName = Prefer(source.ProductName, target.ProductName); target.Specification = Prefer(source.Specification, target.Specification);
            target.SearchText = Prefer(source.SearchText, target.SearchText); target.Source = Prefer(source.Source, target.Source);
            target.SourceYear = Max(source.SourceYear, target.SourceYear); target.IsManuallyVerified |= source.IsManuallyVerified;
            target.UseCount = Math.Max(target.UseCount, source.UseCount); target.RejectedCount = Math.Max(target.RejectedCount, source.RejectedCount);
            target.LastUsedAt = Max(source.LastUsedAt, target.LastUsedAt); target.UpdatedAt = Max(source.UpdatedAt, target.UpdatedAt);
            if (source.IsManuallyVerified) target.ResolutionStatus = source.ResolutionStatus;
        }

        private static HsCode CloneHsCode(HsCode source) => new()
        {
            Id = source?.Id ?? 0,
            Code = source?.Code ?? string.Empty,
            Name = source?.Name ?? string.Empty,
            Unit = source?.Unit,
            Description = source?.Description,
            Elements = source?.Elements,
            SupervisionConditions = source?.SupervisionConditions,
            InspectionCategory = source?.InspectionCategory,
            RebateRate = source?.RebateRate,
            UpdateTime = source?.UpdateTime,
            Status = source?.Status,
            SourceName = source?.SourceName,
            EffectiveYear = source?.EffectiveYear,
            LastVerifiedAt = source?.LastVerifiedAt,
            ReplacedByCodes = source?.ReplacedByCodes,
            NormalTariffRate = source?.NormalTariffRate,
            PreferentialTariffRate = source?.PreferentialTariffRate,
            ExportTariffRate = source?.ExportTariffRate,
            ConsumptionTaxRate = source?.ConsumptionTaxRate,
            ValueAddedTaxRate = source?.ValueAddedTaxRate,
            Notes = source?.Notes,
            RowVersion = source?.RowVersion == null ? null : source.RowVersion.ToArray(),
            DetailUrl = source?.DetailUrl
        };

        private static HsCodeDeclarationExample CloneHsCodeDeclarationExample(HsCodeDeclarationExample source) => new()
        {
            Id = source?.Id ?? 0,
            Fingerprint = source?.Fingerprint ?? string.Empty,
            RawReportedHsCode = source?.RawReportedHsCode ?? string.Empty,
            ResolvedCurrentHsCode = source?.ResolvedCurrentHsCode,
            ProductName = source?.ProductName ?? string.Empty,
            Specification = source?.Specification,
            SearchText = source?.SearchText ?? string.Empty,
            Source = source?.Source ?? string.Empty,
            SourceYear = source?.SourceYear,
            ResolutionStatus = source?.ResolutionStatus ?? "Unresolved",
            IsManuallyVerified = source?.IsManuallyVerified ?? false,
            UseCount = source?.UseCount ?? 0,
            RejectedCount = source?.RejectedCount ?? 0,
            LastUsedAt = source?.LastUsedAt,
            CreatedAt = source?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = source?.UpdatedAt ?? DateTime.UtcNow
        };

        private static HsCodeReplacementRelation CloneHsCodeReplacementRelation(HsCodeReplacementRelation source) => new()
        {
            Id = source?.Id ?? 0,
            OldCode = source?.OldCode ?? string.Empty,
            NewCode = source?.NewCode ?? string.Empty,
            EffectiveYear = source?.EffectiveYear,
            Source = source?.Source ?? string.Empty,
            Confidence = source?.Confidence ?? 0,
            IsManuallyVerified = source?.IsManuallyVerified ?? false,
            CreatedAt = source?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = source?.UpdatedAt ?? DateTime.UtcNow
        };

        private static HsCodeSearchFeedback CloneHsCodeSearchFeedback(HsCodeSearchFeedback source) => new()
        {
            Id = source?.Id ?? 0,
            Fingerprint = source?.Fingerprint ?? string.Empty,
            QueryText = source?.QueryText ?? string.Empty,
            ProductName = source?.ProductName,
            Specification = source?.Specification,
            CandidateCode = source?.CandidateCode ?? string.Empty,
            AcceptedCount = source?.AcceptedCount ?? 0,
            RejectedCount = source?.RejectedCount ?? 0,
            LastConfirmedAt = source?.LastConfirmedAt,
            UpdatedAt = source?.UpdatedAt ?? DateTime.UtcNow
        };

        private static string Prefer(string primary, string fallback) => string.IsNullOrWhiteSpace(primary) ? fallback : primary.Trim();

        private static string ValidateTextLength(string value, int maximumLength, string fieldName)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > maximumLength)
                throw new ArgumentException($"{fieldName}不能超过 {maximumLength} 个字符。", fieldName);
            return normalized;
        }

        private static string NormalizeHistoryProductName(string value)
        {
            string name = (value ?? string.Empty).Normalize(NormalizationForm.FormKC).Trim();
            int separator = name.LastIndexOf('-');
            if (separator > 0 && name[(separator + 1)..].All(char.IsDigit))
                return name[..separator].TrimEnd();
            return name;
        }

        private static string JoinHistorySpecification(params string[] values) => string.Join(" · ",
            values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()));

        private static int? Max(int? left, int? right) => !left.HasValue ? right : !right.HasValue ? left : Math.Max(left.Value, right.Value);
        private static DateTime? Max(DateTime? left, DateTime? right) => !left.HasValue ? right : !right.HasValue ? left : left > right ? left : right;
        private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;

        private static async Task<byte[]> ReadEntryAsync(ZipArchive archive, string name, CancellationToken cancellationToken)
        {
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException($"HS知识库缺少文件：{name}。");
            if (entry.Length > MaximumKnowledgeEntryBytes)
            {
                throw new InvalidDataException($"HS知识库文件过大：{name}。");
            }
            await using var stream = entry.Open();
            using var output = new MemoryStream();
            await BoundedStreamHelper.CopyToAsync(
                stream,
                output,
                MaximumKnowledgeEntryBytes,
                cancellationToken);
            return output.ToArray();
        }

        private sealed class HashingQuotaWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            private readonly long _maximumEntryBytes;
            private readonly long _maximumTotalBytes;
            private readonly Func<long> _totalBytesProvider;
            private bool _hashFinalized;

            public HashingQuotaWriteStream(
                Stream inner,
                long maximumEntryBytes,
                long maximumTotalBytes,
                Func<long> totalBytesProvider)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _maximumEntryBytes = maximumEntryBytes;
                _maximumTotalBytes = maximumTotalBytes;
                _totalBytesProvider = totalBytesProvider ?? throw new ArgumentNullException(nameof(totalBytesProvider));
            }

            public long BytesWritten { get; private set; }

            public string GetHashHex()
            {
                if (_hashFinalized)
                {
                    throw new InvalidOperationException("HS知识库导出校验和已读取。");
                }

                _hashFinalized = true;
                return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => BytesWritten;
            public override long Position
            {
                get => BytesWritten;
                set => throw new NotSupportedException();
            }

            public override void Flush() => _inner.Flush();

            public override Task FlushAsync(CancellationToken cancellationToken) =>
                _inner.FlushAsync(cancellationToken);

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) =>
                throw new NotSupportedException();

            public override void SetLength(long value) =>
                throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                ValidateWrite(count);
                _hash.AppendData(buffer, offset, count);
                _inner.Write(buffer, offset, count);
                BytesWritten += count;
            }

            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ValidateWrite(buffer.Length);
                _hash.AppendData(buffer.Span);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesWritten += buffer.Length;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _hash.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ValidateWrite(int count)
            {
                if (count < 0 || BytesWritten > _maximumEntryBytes - count)
                {
                    throw new PayloadLimitExceededException(_maximumEntryBytes);
                }

                long totalBytes = _totalBytesProvider();
                if (totalBytes > _maximumTotalBytes - BytesWritten - count)
                {
                    throw new PayloadLimitExceededException(_maximumTotalBytes);
                }
            }
        }

        private sealed class MaximumLengthWriteStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _maximumBytes;
            private readonly bool _leaveOpen;

            public MaximumLengthWriteStream(Stream inner, long maximumBytes, bool leaveOpen = false)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
                _maximumBytes = maximumBytes;
                _leaveOpen = leaveOpen;
            }

            private long BytesWritten { get; set; }
            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArgumentNullException.ThrowIfNull(buffer);
                ValidateWrite(count);
                _inner.Write(buffer, offset, count);
                BytesWritten += count;
            }

            public override async ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                ValidateWrite(buffer.Length);
                await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
                BytesWritten += buffer.Length;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_leaveOpen)
                {
                    _inner.Dispose();
                }

                base.Dispose(disposing);
            }

            private void ValidateWrite(int count)
            {
                if (count < 0 || BytesWritten > _maximumBytes - count)
                {
                    throw new PayloadLimitExceededException(_maximumBytes);
                }
            }
        }

        private sealed record KnowledgeManifest(string SchemaVersion, DateTimeOffset ExportedAt, DateTimeOffset? Since, Dictionary<string, string> Checksums);
        private readonly record struct ReplacementRelationKey(string OldCode, string NewCode, int? EffectiveYear);
        private sealed record CurrentCodeResolution(string CurrentCode, string Status, IReadOnlyList<string> Replacements, bool CanUse);
        private sealed record KnowledgeCandidate(
            HsCodeDeclarationExample Example,
            CurrentCodeResolution Resolution,
            int Score,
            IReadOnlyList<string> MatchReasons,
            IReadOnlyList<string> ConflictWarnings);
        private sealed record AttributeAssessment(int Penalty, IReadOnlyList<string> MatchReasons, IReadOnlyList<string> ConflictWarnings);
        private sealed record HistorySourceRow(string Code, string Name, string Specification, string Source, string Variant);
        private sealed record HistorySourceProjection(
            string Code,
            string NamePrimary,
            string NameFallback,
            string SpecificationOne,
            string SpecificationTwo,
            string SpecificationThree,
            string SpecificationFour,
            string Source,
            string Variant);
        private sealed record HistorySourceReadResult(IReadOnlyList<HistorySourceProjection> Rows, bool HasMore);
        private sealed record HistoryCandidateGroup(
            string Fingerprint,
            string RawCode,
            string ProductName,
            string Specification,
            string Source,
            int SourceCount,
            int VariantCount,
            IReadOnlyList<string> VariantSamples);
    }
}

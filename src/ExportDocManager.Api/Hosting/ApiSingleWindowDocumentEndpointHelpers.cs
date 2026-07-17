using System.Reflection;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.SingleWindow;
using ExportDocManager.Utils;

namespace ExportDocManager.Api.Hosting
{
    public static partial class ApiEndpointRouteBuilderExtensions
    {
        private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomsCooDocumentStringProperties =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(CustomsCooDocument));

        private static readonly IReadOnlyDictionary<string, PropertyInfo> CustomsCooItemStringProperties =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(CustomsCooItem));

        private static readonly IReadOnlyDictionary<string, PropertyInfo> AgentConsignmentDocumentStringProperties =
            SingleWindowEditorFieldHelper.BuildPublicStringPropertyMap(typeof(AgentConsignmentDocument));

        private static async Task<IResult> GetCustomsCooDocumentAsync(
            ICustomsCooDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            bool buildDefaults,
            CancellationToken cancellationToken)
        {
            try
            {
                await settingsService.LoadAsync();
                var document = buildDefaults
                    ? await documentService.BuildDefaultsAsync(invoiceId, cancellationToken)
                    : await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromCustomsCooDocument(document));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> SaveCustomsCooDocumentAsync(
            ICustomsCooDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            ApiCustomsCooDocumentDto request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("海关原产地证草稿请求体不能为空。"));
            }

            if (request.SourceInvoiceId > 0 && request.SourceInvoiceId != invoiceId)
            {
                return Results.BadRequest(new ApiErrorResponse("请求体来源发票ID与路径ID不一致。"));
            }

            try
            {
                await settingsService.LoadAsync();
                var document = ApiSingleWindowDtoFactory.ToCustomsCooDocument(request, invoiceId);
                int savedId = await documentService.SaveAsync(document, cancellationToken);
                var refreshed = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                return Results.Ok(new ApiCustomsCooDocumentSaveResponse(
                    true,
                    savedId,
                    ApiSingleWindowDtoFactory.FromCustomsCooDocument(refreshed),
                    "海关原产地证草稿已保存。"));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> GetAgentConsignmentDocumentAsync(
            IAgentConsignmentDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            bool buildDefaults,
            CancellationToken cancellationToken)
        {
            try
            {
                await settingsService.LoadAsync();
                var document = buildDefaults
                    ? await documentService.BuildDefaultsAsync(invoiceId, cancellationToken)
                    : await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                return Results.Ok(ApiSingleWindowDtoFactory.FromAgentConsignmentDocument(document));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> GetCustomsCooLockedFieldsAsync(
            ICustomsCooDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            CancellationToken cancellationToken)
        {
            try
            {
                await settingsService.LoadAsync();
                var current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                var defaults = await documentService.BuildDefaultsAsync(invoiceId, cancellationToken);
                var fields = SingleWindowDraftStateHelper.DescribeCustomsCooLockedFields(current, defaults);
                return Results.Ok(new ApiSingleWindowLockedFieldsResponse(
                    fields.Count,
                    ToApiLockedFields(fields)));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> UnlockCustomsCooFieldsAsync(
            ICustomsCooDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            ApiSingleWindowUnlockFieldsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("解锁字段请求体不能为空。"));
            }

            var requestedKeys = NormalizeSingleWindowFieldKeys(request.FieldKeys);
            if (requestedKeys.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("请至少选择一个要解锁的字段。"));
            }

            try
            {
                await settingsService.LoadAsync();
                var current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                var defaults = await documentService.BuildDefaultsAsync(invoiceId, cancellationToken);
                var lockedFields = SingleWindowDraftStateHelper.DescribeCustomsCooLockedFields(current, defaults);
                var lockedKeys = lockedFields.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
                int changedCount = requestedKeys
                    .Where(lockedKeys.Contains)
                    .Sum(key => RestoreCustomsCooLockedField(current, defaults, key));

                if (changedCount > 0)
                {
                    await documentService.SaveAsync(current, cancellationToken);
                    current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                }

                var refreshedFields = SingleWindowDraftStateHelper.DescribeCustomsCooLockedFields(current, defaults);
                return Results.Ok(new ApiCustomsCooUnlockFieldsResponse(
                    true,
                    changedCount,
                    ApiSingleWindowDtoFactory.FromCustomsCooDocument(current),
                    ToApiLockedFields(refreshedFields),
                    changedCount > 0
                        ? $"已将 {changedCount} 个字段恢复为当前建议值。"
                        : "所选字段当前已经与建议值一致，无需解锁。"));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> SaveAgentConsignmentDocumentAsync(
            IAgentConsignmentDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            ApiAgentConsignmentDocumentDto request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("报关代理委托草稿请求体不能为空。"));
            }

            if (request.SourceInvoiceId > 0 && request.SourceInvoiceId != invoiceId)
            {
                return Results.BadRequest(new ApiErrorResponse("请求体来源发票ID与路径ID不一致。"));
            }

            try
            {
                await settingsService.LoadAsync();
                var document = ApiSingleWindowDtoFactory.ToAgentConsignmentDocument(request, invoiceId);
                int savedId = await documentService.SaveAsync(document, cancellationToken);
                var refreshed = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                return Results.Ok(new ApiAgentConsignmentDocumentSaveResponse(
                    true,
                    savedId,
                    ApiSingleWindowDtoFactory.FromAgentConsignmentDocument(refreshed),
                    "报关代理委托草稿已保存。"));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> GetAgentConsignmentLockedFieldsAsync(
            IAgentConsignmentDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            CancellationToken cancellationToken)
        {
            try
            {
                await settingsService.LoadAsync();
                var current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                var defaults = await documentService.BuildDefaultsAsync(invoiceId, cancellationToken);
                var fields = SingleWindowDraftStateHelper.DescribeAgentConsignmentLockedFields(current, defaults);
                return Results.Ok(new ApiSingleWindowLockedFieldsResponse(
                    fields.Count,
                    ToApiLockedFields(fields)));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static async Task<IResult> UnlockAgentConsignmentFieldsAsync(
            IAgentConsignmentDocumentService documentService,
            ISettingsService settingsService,
            int invoiceId,
            ApiSingleWindowUnlockFieldsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                return Results.BadRequest(new ApiErrorResponse("解锁字段请求体不能为空。"));
            }

            var requestedKeys = NormalizeSingleWindowFieldKeys(request.FieldKeys);
            if (requestedKeys.Count == 0)
            {
                return Results.BadRequest(new ApiErrorResponse("请至少选择一个要解锁的字段。"));
            }

            try
            {
                await settingsService.LoadAsync();
                var current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                var defaults = await documentService.BuildDefaultsAsync(invoiceId, cancellationToken);
                var lockedFields = SingleWindowDraftStateHelper.DescribeAgentConsignmentLockedFields(current, defaults);
                var lockedKeys = lockedFields.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
                int changedCount = requestedKeys
                    .Where(lockedKeys.Contains)
                    .Sum(key => RestoreAgentConsignmentLockedField(current, defaults, key));

                if (changedCount > 0)
                {
                    await documentService.SaveAsync(current, cancellationToken);
                    current = await documentService.GetOrCreateAsync(invoiceId, cancellationToken);
                }

                var refreshedFields = SingleWindowDraftStateHelper.DescribeAgentConsignmentLockedFields(current, defaults);
                return Results.Ok(new ApiAgentConsignmentUnlockFieldsResponse(
                    true,
                    changedCount,
                    ApiSingleWindowDtoFactory.FromAgentConsignmentDocument(current),
                    ToApiLockedFields(refreshedFields),
                    changedCount > 0
                        ? $"已将 {changedCount} 个字段恢复为当前建议值。"
                        : "所选字段当前已经与建议值一致，无需解锁。"));
            }
            catch (InvalidOperationException ex) when (IsSingleWindowMissingSource(ex))
            {
                return Results.NotFound(new ApiErrorResponse(ex.Message));
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new ApiErrorResponse(ex.Message),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return WriteConflict(ex.Message);
            }
        }

        private static int RestoreCustomsCooLockedField(
            CustomsCooDocument current,
            CustomsCooDocument defaults,
            string key)
        {
            if (SingleWindowDraftStateHelper.TryParseGoodsFieldKey(key, out var identity, out var propertyName))
            {
                return RestoreCustomsCooGoodsField(current, defaults, identity, propertyName);
            }

            string suggestedValue = SingleWindowEditorFieldHelper.GetStringPropertyValue(
                defaults,
                CustomsCooDocumentStringProperties,
                key,
                value => NormalizeCustomsCooValue(key, value));
            return SingleWindowEditorFieldHelper.ApplyStringPropertyChange(
                current,
                CustomsCooDocumentStringProperties,
                key,
                suggestedValue,
                value => NormalizeCustomsCooValue(key, value));
        }

        private static int RestoreCustomsCooGoodsField(
            CustomsCooDocument current,
            CustomsCooDocument defaults,
            string identity,
            string propertyName)
        {
            var targetRow = (current?.Items ?? [])
                .FirstOrDefault(row =>
                    row != null &&
                    string.Equals(
                        SingleWindowDraftStateHelper.GetGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo),
                        identity,
                        StringComparison.Ordinal));
            if (targetRow == null)
            {
                return 0;
            }

            var defaultRow = (defaults?.Items ?? [])
                .FirstOrDefault(row =>
                    row != null &&
                    string.Equals(
                        SingleWindowDraftStateHelper.GetGoodsIdentity(row.SourceItemId, row.SourceStyleNo, row.GNo),
                        identity,
                        StringComparison.Ordinal));
            string suggestedValue = SingleWindowEditorFieldHelper.GetStringPropertyValue(
                defaultRow,
                CustomsCooItemStringProperties,
                propertyName);
            return SingleWindowEditorFieldHelper.ApplyStringPropertyChange(
                targetRow,
                CustomsCooItemStringProperties,
                propertyName,
                suggestedValue);
        }

        private static int RestoreAgentConsignmentLockedField(
            AgentConsignmentDocument current,
            AgentConsignmentDocument defaults,
            string key)
        {
            string suggestedValue = SingleWindowEditorFieldHelper.GetStringPropertyValue(
                defaults,
                AgentConsignmentDocumentStringProperties,
                key,
                value => NormalizeAgentConsignmentValue(key, value));
            return SingleWindowEditorFieldHelper.ApplyStringPropertyChange(
                current,
                AgentConsignmentDocumentStringProperties,
                key,
                suggestedValue,
                value => NormalizeAgentConsignmentValue(key, value));
        }

        private static IReadOnlyList<string> NormalizeSingleWindowFieldKeys(IEnumerable<string> fieldKeys)
        {
            return (fieldKeys ?? Array.Empty<string>())
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Select(key => key.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static IReadOnlyList<ApiSingleWindowLockedFieldDto> ToApiLockedFields(
            IEnumerable<SingleWindowLockedFieldDetail> fields)
        {
            return (fields ?? Array.Empty<SingleWindowLockedFieldDetail>())
                .Select(item => new ApiSingleWindowLockedFieldDto(
                    item.Key,
                    item.DisplayName,
                    item.CurrentValue,
                    item.SuggestedValue))
                .ToList();
        }

        private static string NormalizeCustomsCooValue(string propertyName, string value)
        {
            string normalized = SingleWindowEditorFieldHelper.NormalizeStringValue(value);
            return string.Equals(propertyName, nameof(CustomsCooDocument.PriceTerms), StringComparison.Ordinal)
                ? SingleWindowFieldMapperHelpers.NormalizePriceTerms(normalized)
                : normalized;
        }

        private static string NormalizeAgentConsignmentValue(string propertyName, string value)
        {
            string normalized = SingleWindowEditorFieldHelper.NormalizeStringValue(value);
            return string.Equals(propertyName, nameof(AgentConsignmentDocument.OperType), StringComparison.Ordinal) &&
                   string.IsNullOrWhiteSpace(normalized)
                ? "1"
                : normalized;
        }
    }
}

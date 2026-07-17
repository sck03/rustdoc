namespace ExportDocManager.Api.Hosting
{
    public static partial class OpenApiDocumentFactory
    {
        private static Dictionary<string, object> CreateToolsSchemas() =>
            new Dictionary<string, object>
            {
                        ["ApiShutdownMaintenanceResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "deletedAuditLogs",
                                "deletedTextLogs",
                                "uploadedBackupFileName",
                                "cloudSyncErrorMessage",
                                "cloudSyncFailed",
                                "backupRoot",
                                "logRoot",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Desktop shutdown maintenance result message."),
                                ["deletedAuditLogs"] = new { type = "integer", format = "int32" },
                                ["deletedTextLogs"] = new { type = "integer", format = "int32" },
                                ["uploadedBackupFileName"] = StringProperty("Latest runtime backup ZIP uploaded to WebDAV, when enabled."),
                                ["cloudSyncErrorMessage"] = StringProperty("Non-blocking WebDAV upload error message."),
                                ["cloudSyncFailed"] = new { type = "boolean" },
                                ["backupRoot"] = StringProperty("Runtime data root Backups directory."),
                                ["logRoot"] = StringProperty("Program root logs directory."),
                                ["storagePolicy"] = StringProperty("Shutdown maintenance storage and data-domain policy.")
                            }
                        },
                        ["ApiPostgreSqlMaintenanceStatusResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "postgreSqlSelected",
                                "postgreSqlConfigured",
                                "host",
                                "port",
                                "database",
                                "username",
                                "backupRoot",
                                "toolBinRoot",
                                "pgDumpPath",
                                "pgRestorePath",
                                "psqlPath",
                                "toolsReady",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["postgreSqlSelected"] = new { type = "boolean" },
                                ["postgreSqlConfigured"] = new { type = "boolean" },
                                ["host"] = StringProperty("Configured PostgreSQL server host."),
                                ["port"] = new { type = "integer", format = "int32" },
                                ["database"] = StringProperty("Configured PostgreSQL business database."),
                                ["username"] = StringProperty("Configured application database role."),
                                ["backupRoot"] = StringProperty("Runtime data root Backups/PostgreSQL directory."),
                                ["toolBinRoot"] = StringProperty("Resolved PostgreSQL client tool directory."),
                                ["pgDumpPath"] = StringProperty("Resolved pg_dump path."),
                                ["pgRestorePath"] = StringProperty("Resolved pg_restore path."),
                                ["psqlPath"] = StringProperty("Resolved psql path."),
                                ["toolsReady"] = new { type = "boolean" },
                                ["storagePolicy"] = StringProperty("Path and storage policy for PostgreSQL maintenance.")
                            }
                        },
                        ["ApiOcrRecognizeImageRequest"] = new
                        {
                            type = "object",
                            required = new[] { "filePath" },
                            properties = new Dictionary<string, object>
                            {
                                ["filePath"] = StringProperty("User-selected image source path. The sidecar does not choose a default system-drive path.")
                            }
                        },
                        ["ApiOcrRecognizeImageContentRequest"] = new
                        {
                            type = "object",
                            required = new[] { "imageContentBase64" },
                            properties = new Dictionary<string, object>
                            {
                                ["imageContentBase64"] = StringProperty("Base64 encoded image content, optionally as a data URL. Clipboard and other pathless sources stay in request memory and are not written to temporary files."),
                                ["sourceName"] = StringProperty("Display name for the in-memory image source, such as clipboard image."),
                                ["sourceMimeType"] = StringProperty("Optional image/* MIME type for validation and audit display.")
                            }
                        },
                        ["ApiOcrRecognizeImageResponse"] = new
                        {
                            type = "object",
                            required = new[] { "sourcePath", "fullText", "lines", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["sourcePath"] = StringProperty("Normalized source image path that was read."),
                                ["fullText"] = StringProperty("Recognized full text returned in memory."),
                                ["lines"] = RefArraySchema("ApiOcrLineDto"),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiOcrLineDto"] = new
                        {
                            type = "object",
                            required = new[] { "text", "x", "y", "width", "height" },
                            properties = new Dictionary<string, object>
                            {
                                ["text"] = StringProperty("Recognized line text."),
                                ["x"] = new { type = "integer", format = "int32", description = "Line bounding box X coordinate in source image pixels." },
                                ["y"] = new { type = "integer", format = "int32", description = "Line bounding box Y coordinate in source image pixels." },
                                ["width"] = new { type = "integer", format = "int32", description = "Line bounding box width in source image pixels." },
                                ["height"] = new { type = "integer", format = "int32", description = "Line bounding box height in source image pixels." }
                            }
                        },
                        ["ApiExchangeRateListResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "rates",
                                "sourceUrl",
                                "selectedCurrencies",
                                "cacheDurationMinutes",
                                "fetchedAt",
                                "statusText",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["rates"] = RefArraySchema("ApiExchangeRateDto"),
                                ["sourceUrl"] = StringProperty("Configured remote exchange-rate source URL."),
                                ["selectedCurrencies"] = StringArrayProperty("Currencies selected in settings, or the supported list when no selection is configured."),
                                ["cacheDurationMinutes"] = new { type = "integer", format = "int32", description = "Configured in-memory exchange-rate cache duration." },
                                ["fetchedAt"] = new { type = "string", format = "date-time", description = "Local sidecar time when the response was produced." },
                                ["statusText"] = StringProperty("User-facing load status matching the legacy WinForms exchange-rate screen."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiExchangeRateAvailableCurrenciesResponse"] = new
                        {
                            type = "object",
                            required = new[] { "currencies", "sourceUrl", "fetchedAt", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["currencies"] = StringArrayProperty("Currency names reported by the configured exchange-rate source."),
                                ["sourceUrl"] = StringProperty("Configured remote exchange-rate source URL."),
                                ["fetchedAt"] = new { type = "string", format = "date-time", description = "Local sidecar time when the response was produced." },
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiEmailStatusResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "isConfigured",
                                "smtpHost",
                                "smtpPort",
                                "enableSsl",
                                "fromAddress",
                                "fromDisplayName",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["isConfigured"] = new { type = "boolean" },
                                ["smtpHost"] = StringProperty("Configured SMTP host."),
                                ["smtpPort"] = new { type = "integer", format = "int32" },
                                ["enableSsl"] = new { type = "boolean" },
                                ["fromAddress"] = StringProperty("Resolved sender address."),
                                ["fromDisplayName"] = StringProperty("Configured sender display name."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiEmailServerSuggestionRequest"] = new
                        {
                            type = "object",
                            required = new[] { "emailAddress" },
                            properties = new Dictionary<string, object>
                            {
                                ["emailAddress"] = StringProperty("Email address used to infer the SMTP server settings.")
                            }
                        },
                        ["ApiEmailServerSuggestionResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "emailAddress",
                                "smtpHost",
                                "smtpPort",
                                "enableSsl",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("SMTP suggestion result message."),
                                ["emailAddress"] = StringProperty("Normalized email address used for the suggestion."),
                                ["smtpHost"] = StringProperty("Suggested SMTP host."),
                                ["smtpPort"] = new { type = "integer", format = "int32" },
                                ["enableSsl"] = new { type = "boolean" },
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiEmailSendRequest"] = new
                        {
                            type = "object",
                            required = new[] { "toAddress", "subject", "body", "attachmentPaths" },
                            properties = new Dictionary<string, object>
                            {
                                ["toAddress"] = StringProperty("Recipient email address."),
                                ["subject"] = StringProperty("Email subject."),
                                ["body"] = StringProperty("Email HTML body."),
                                ["attachmentPaths"] = StringArrayProperty("Explicit attachment file paths selected by the user.")
                            }
                        },
                        ["ApiEmailSendResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "toAddress",
                                "subject",
                                "attachmentCount",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("Send result message."),
                                ["toAddress"] = StringProperty("Recipient email address."),
                                ["subject"] = StringProperty("Email subject."),
                                ["attachmentCount"] = new { type = "integer", format = "int32" },
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiEmailTestResponse"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "success",
                                "message",
                                "fromAddress",
                                "smtpHost",
                                "storagePolicy"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["message"] = StringProperty("SMTP test result message."),
                                ["fromAddress"] = StringProperty("Recipient/sender address used for the test message."),
                                ["smtpHost"] = StringProperty("SMTP host used for the test message."),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiExchangeRateDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "currencyName",
                                "buyingRate",
                                "cashBuyingRate",
                                "sellingRate",
                                "cashSellingRate",
                                "middleRate",
                                "publishTime"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["currencyName"] = StringProperty("Currency display name."),
                                ["buyingRate"] = NullableDecimalProperty("Spot buying rate."),
                                ["cashBuyingRate"] = NullableDecimalProperty("Cash buying rate."),
                                ["sellingRate"] = NullableDecimalProperty("Spot selling rate."),
                                ["cashSellingRate"] = NullableDecimalProperty("Cash selling rate."),
                                ["middleRate"] = NullableDecimalProperty("BOC conversion middle rate."),
                                ["publishTime"] = StringProperty("Publish time reported by the source page.")
                            }
                        },
                        ["ApiContainerPackingAnalyzeRequest"] = new
                        {
                            type = "object",
                            required = new[] { "container", "cargoItems", "rules" },
                            properties = new Dictionary<string, object>
                            {
                                ["container"] = RefSchema("ApiContainerDimensionsDto"),
                                ["cargoItems"] = RefArraySchema("ApiContainerPackingCargoInputDto"),
                                ["rules"] = RefSchema("ApiContainerPackingRulesDto")
                            }
                        },
                        ["ApiContainerDimensionsDto"] = new
                        {
                            type = "object",
                            required = new[] { "length", "width", "height", "volume", "maxWeight" },
                            properties = new Dictionary<string, object>
                            {
                                ["length"] = new { type = "integer", format = "int32", description = "Container inner length in centimeters." },
                                ["width"] = new { type = "integer", format = "int32", description = "Container inner width in centimeters." },
                                ["height"] = new { type = "integer", format = "int32", description = "Container inner height in centimeters." },
                                ["volume"] = DecimalProperty("Container volume in cubic meters."),
                                ["maxWeight"] = DecimalProperty("Container maximum load weight in kilograms.")
                            }
                        },
                        ["ApiContainerPackingCargoInputDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "name",
                                "length",
                                "width",
                                "height",
                                "weight",
                                "quantity",
                                "colorArgb",
                                "usePallet",
                                "unitsPerPallet",
                                "maxTopLoadWeight",
                                "preferredZone",
                                "loadSequence",
                                "priorityGroup"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["name"] = StringProperty("Cargo display name."),
                                ["length"] = DecimalProperty("Cargo length in centimeters."),
                                ["width"] = DecimalProperty("Cargo width in centimeters."),
                                ["height"] = DecimalProperty("Cargo height in centimeters."),
                                ["weight"] = DecimalProperty("Cargo weight per carton or load in kilograms."),
                                ["quantity"] = new { type = "integer", format = "int32", description = "Cargo carton quantity." },
                                ["colorArgb"] = new { type = "integer", format = "int32", description = "Display color encoded as signed ARGB integer." },
                                ["usePallet"] = new { type = "boolean", description = "Whether the cargo should be converted into pallet loads." },
                                ["unitsPerPallet"] = new { type = "integer", format = "int32", description = "Cartons per pallet when pallet constraints are enabled." },
                                ["maxTopLoadWeight"] = DecimalProperty("Maximum top load weight this cargo can support. Zero disables the limit."),
                                ["preferredZone"] = StringProperty("Preferred container zone: Auto, Head, Middle, or Door."),
                                ["loadSequence"] = new { type = "integer", format = "int32", description = "Loading sequence priority starting from 1." },
                                ["priorityGroup"] = StringProperty("Optional grouping key for floor row and stack ordering.")
                            }
                        },
                        ["ApiContainerPackingRulesDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "allowRotation",
                                "usePalletConstraints",
                                "defaultPalletLength",
                                "defaultPalletWidth",
                                "defaultPalletHeight",
                                "defaultPalletWeight",
                                "enforceCenterOfGravity",
                                "centerOfGravityTolerancePercent",
                                "minimumSupportAreaPercent",
                                "requireSameFootprintStacking"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["allowRotation"] = new { type = "boolean", description = "Allow rotating cargo footprint." },
                                ["usePalletConstraints"] = new { type = "boolean", description = "Convert palletized cargo into pallet loads." },
                                ["defaultPalletLength"] = new { type = "integer", format = "int32", description = "Default pallet length in centimeters." },
                                ["defaultPalletWidth"] = new { type = "integer", format = "int32", description = "Default pallet width in centimeters." },
                                ["defaultPalletHeight"] = new { type = "integer", format = "int32", description = "Default pallet height in centimeters." },
                                ["defaultPalletWeight"] = DecimalProperty("Default pallet weight in kilograms."),
                                ["enforceCenterOfGravity"] = new { type = "boolean", description = "Reject placements outside the configured center-of-gravity tolerance." },
                                ["centerOfGravityTolerancePercent"] = DecimalProperty("Center-of-gravity tolerance percentage."),
                                ["minimumSupportAreaPercent"] = DecimalProperty("Minimum stacked support area percentage."),
                                ["requireSameFootprintStacking"] = new { type = "boolean", description = "Require stacked loads to match the supporting footprint." }
                            }
                        },
                        ["ApiContainerPackingAnalyzeResponse"] = new
                        {
                            type = "object",
                            required = new[] { "analysis", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["analysis"] = RefSchema("ApiContainerPackingAnalysisDto"),
                                ["storagePolicy"] = StringProperty("Path and storage policy for audit/review.")
                            }
                        },
                        ["ApiContainerPackingProjectListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "projects", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["projects"] = RefArraySchema("ApiContainerPackingProjectSummaryDto"),
                                ["storagePolicy"] = StringProperty("Runtime storage and data-domain policy for saved container packing projects.")
                            }
                        },
                        ["ApiContainerPackingProjectResponse"] = new
                        {
                            type = "object",
                            required = new[] { "project", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["project"] = RefSchema("ApiContainerPackingProjectDto"),
                                ["storagePolicy"] = StringProperty("Runtime storage and data-domain policy for saved container packing projects.")
                            }
                        },
                        ["ApiContainerPackingProjectSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "containerType", "container", "rules", "cargoItems" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32", description = "Existing project id. Zero creates a new project." },
                                ["name"] = StringProperty("Project name."),
                                ["containerType"] = StringProperty("Container type display name, such as 20GP or 40HQ."),
                                ["container"] = RefSchema("ApiContainerDimensionsDto"),
                                ["rules"] = RefSchema("ApiContainerPackingRulesDto"),
                                ["cargoItems"] = RefArraySchema("ApiContainerPackingCargoInputDto")
                            }
                        },
                        ["ApiContainerPackingProjectSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "project", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["project"] = RefSchema("ApiContainerPackingProjectDto"),
                                ["message"] = StringProperty("Save result message."),
                                ["storagePolicy"] = StringProperty("Runtime storage and data-domain policy for saved container packing projects.")
                            }
                        },
                        ["ApiContainerPackingProjectSummaryDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "containerType", "createdAt", "updatedAt" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Project name."),
                                ["containerType"] = StringProperty("Container type display name."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" }
                            }
                        },
                        ["ApiContainerPackingProjectDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "id",
                                "name",
                                "containerType",
                                "createdAt",
                                "updatedAt",
                                "container",
                                "rules",
                                "cargoItems"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Project name."),
                                ["containerType"] = StringProperty("Container type display name."),
                                ["createdAt"] = new { type = "string", format = "date-time" },
                                ["updatedAt"] = new { type = "string", format = "date-time" },
                                ["container"] = RefSchema("ApiContainerDimensionsDto"),
                                ["rules"] = RefSchema("ApiContainerPackingRulesDto"),
                                ["cargoItems"] = RefArraySchema("ApiContainerPackingCargoInputDto")
                            }
                        },
                        ["ApiContainerTypeListResponse"] = new
                        {
                            type = "object",
                            required = new[] { "containerTypes", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["containerTypes"] = RefArraySchema("ApiContainerTypeDto"),
                                ["storagePolicy"] = StringProperty("Runtime storage and data-domain policy for container type definitions.")
                            }
                        },
                        ["ApiContainerTypeSaveRequest"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "length", "width", "height", "maxVolume", "maxWeight" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32", description = "Existing container type id. Zero creates a new custom type." },
                                ["name"] = StringProperty("Container type display name."),
                                ["length"] = new { type = "integer", format = "int32", description = "Container inner length in centimeters." },
                                ["width"] = new { type = "integer", format = "int32", description = "Container inner width in centimeters." },
                                ["height"] = new { type = "integer", format = "int32", description = "Container inner height in centimeters." },
                                ["maxVolume"] = DecimalProperty("Container volume in cubic meters."),
                                ["maxWeight"] = DecimalProperty("Container maximum load weight in kilograms.")
                            }
                        },
                        ["ApiContainerTypeSaveResponse"] = new
                        {
                            type = "object",
                            required = new[] { "success", "id", "containerType", "message", "storagePolicy" },
                            properties = new Dictionary<string, object>
                            {
                                ["success"] = new { type = "boolean" },
                                ["id"] = new { type = "integer", format = "int32" },
                                ["containerType"] = RefSchema("ApiContainerTypeDto"),
                                ["message"] = StringProperty("Save result message."),
                                ["storagePolicy"] = StringProperty("Runtime storage and data-domain policy for container type definitions.")
                            }
                        },
                        ["ApiContainerTypeDto"] = new
                        {
                            type = "object",
                            required = new[] { "id", "name", "length", "width", "height", "maxVolume", "maxWeight", "isSystemDefault" },
                            properties = new Dictionary<string, object>
                            {
                                ["id"] = new { type = "integer", format = "int32" },
                                ["name"] = StringProperty("Container type display name."),
                                ["length"] = new { type = "integer", format = "int32", description = "Container inner length in centimeters." },
                                ["width"] = new { type = "integer", format = "int32", description = "Container inner width in centimeters." },
                                ["height"] = new { type = "integer", format = "int32", description = "Container inner height in centimeters." },
                                ["maxVolume"] = DecimalProperty("Container volume in cubic meters."),
                                ["maxWeight"] = DecimalProperty("Container maximum load weight in kilograms."),
                                ["isSystemDefault"] = new { type = "boolean", description = "True for seeded default container types." }
                            }
                        },
                        ["ApiContainerPackingAnalysisDto"] = new
                        {
                            type = "object",
                            required = new[]
                            {
                                "packedItems",
                                "totalPackages",
                                "packedPackages",
                                "unpackedPackages",
                                "totalPallets",
                                "packedPallets",
                                "totalVolume",
                                "totalWeight",
                                "packedVolume",
                                "packedWeight",
                                "volumeUtilizationPercent",
                                "weightUtilizationPercent",
                                "containersNeededByVolume",
                                "containersNeededByWeight",
                                "estimatedContainerCount",
                                "centerOfGravityX",
                                "centerOfGravityY",
                                "centerOfGravityLengthDeviationPercent",
                                "centerOfGravityWidthDeviationPercent",
                                "isCenterOfGravityWithinTolerance"
                            },
                            properties = new Dictionary<string, object>
                            {
                                ["packedItems"] = RefArraySchema("ApiPackedCargoItemDto"),
                                ["totalPackages"] = new { type = "integer", format = "int32" },
                                ["packedPackages"] = new { type = "integer", format = "int32" },
                                ["unpackedPackages"] = new { type = "integer", format = "int32" },
                                ["totalPallets"] = new { type = "integer", format = "int32" },
                                ["packedPallets"] = new { type = "integer", format = "int32" },
                                ["totalVolume"] = DecimalProperty("Total cargo volume in cubic meters."),
                                ["totalWeight"] = DecimalProperty("Total cargo weight in kilograms."),
                                ["packedVolume"] = DecimalProperty("Packed cargo volume in cubic meters."),
                                ["packedWeight"] = DecimalProperty("Packed cargo weight in kilograms."),
                                ["volumeUtilizationPercent"] = DecimalProperty("Volume utilization percentage."),
                                ["weightUtilizationPercent"] = DecimalProperty("Weight utilization percentage."),
                                ["containersNeededByVolume"] = new { type = "integer", format = "int32" },
                                ["containersNeededByWeight"] = new { type = "integer", format = "int32" },
                                ["estimatedContainerCount"] = new { type = "integer", format = "int32" },
                                ["centerOfGravityX"] = DecimalProperty("Center of gravity X coordinate in centimeters."),
                                ["centerOfGravityY"] = DecimalProperty("Center of gravity Y coordinate in centimeters."),
                                ["centerOfGravityLengthDeviationPercent"] = DecimalProperty("Length-axis center-of-gravity deviation percentage."),
                                ["centerOfGravityWidthDeviationPercent"] = DecimalProperty("Width-axis center-of-gravity deviation percentage."),
                                ["isCenterOfGravityWithinTolerance"] = new { type = "boolean" }
                            }
                        },
            };
    }
}
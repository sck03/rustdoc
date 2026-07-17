using ExportDocManager.Models;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiContainerPackingProjectDtoFactory
    {
        public const string StoragePolicy =
            "装柜项目/柜型保存仅写入运行目录数据根下的业务数据库表 ContainerProjects、ContainerProjectItems、ContainerTypeDefinitions；不会读写发票、报关单据、付款或报销数据，也不会默认写入系统盘或系统用户数据目录。";

        public static ApiContainerPackingProjectSummaryDto FromProjectSummary(ContainerProject project)
        {
            ArgumentNullException.ThrowIfNull(project);

            return new ApiContainerPackingProjectSummaryDto
            {
                Id = project.Id,
                Name = project.Name ?? string.Empty,
                ContainerType = project.ContainerType ?? string.Empty,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt
            };
        }

        public static ApiContainerPackingProjectDto FromProject(
            ContainerProject project,
            IReadOnlyCollection<ContainerProjectItem> items)
        {
            ArgumentNullException.ThrowIfNull(project);

            return new ApiContainerPackingProjectDto
            {
                Id = project.Id,
                Name = project.Name ?? string.Empty,
                ContainerType = project.ContainerType ?? string.Empty,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt,
                Container = new ApiContainerDimensionsDto
                {
                    Length = project.ContainerLength,
                    Width = project.ContainerWidth,
                    Height = project.ContainerHeight,
                    Volume = project.ContainerMaxVolume,
                    MaxWeight = project.ContainerMaxWeight
                },
                Rules = new ApiContainerPackingRulesDto
                {
                    AllowRotation = project.AllowRotation,
                    UsePalletConstraints = project.UsePalletConstraints,
                    DefaultPalletLength = project.DefaultPalletLength,
                    DefaultPalletWidth = project.DefaultPalletWidth,
                    DefaultPalletHeight = project.DefaultPalletHeight,
                    DefaultPalletWeight = project.DefaultPalletWeight,
                    EnforceCenterOfGravity = project.EnforceCenterOfGravity,
                    CenterOfGravityTolerancePercent = project.CenterOfGravityTolerancePercent,
                    MinimumSupportAreaPercent = project.MinimumSupportAreaPercent,
                    RequireSameFootprintStacking = project.RequireSameFootprintStacking
                },
                CargoItems = (items ?? Array.Empty<ContainerProjectItem>())
                    .Select(FromProjectItem)
                    .ToList()
            };
        }

        public static ContainerProject ToProject(ApiContainerPackingProjectSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var container = request.Container ?? new ApiContainerDimensionsDto();
            var rules = request.Rules ?? new ApiContainerPackingRulesDto();

            return new ContainerProject
            {
                Id = Math.Max(request.Id, 0),
                Name = NormalizeName(request.Name, "未命名方案"),
                ContainerType = NormalizeName(request.ContainerType, string.Empty),
                ContainerLength = Math.Max(container.Length, 0),
                ContainerWidth = Math.Max(container.Width, 0),
                ContainerHeight = Math.Max(container.Height, 0),
                ContainerMaxVolume = Math.Max(container.Volume, 0m),
                ContainerMaxWeight = Math.Max(container.MaxWeight, 0m),
                AllowRotation = rules.AllowRotation,
                UsePalletConstraints = rules.UsePalletConstraints,
                DefaultPalletLength = Math.Max(rules.DefaultPalletLength, 1),
                DefaultPalletWidth = Math.Max(rules.DefaultPalletWidth, 1),
                DefaultPalletHeight = Math.Max(rules.DefaultPalletHeight, 0),
                DefaultPalletWeight = Math.Max(rules.DefaultPalletWeight, 0m),
                EnforceCenterOfGravity = rules.EnforceCenterOfGravity,
                CenterOfGravityTolerancePercent = Math.Max(rules.CenterOfGravityTolerancePercent, 0m),
                MinimumSupportAreaPercent = Math.Clamp(rules.MinimumSupportAreaPercent, 0m, 100m),
                RequireSameFootprintStacking = rules.RequireSameFootprintStacking
            };
        }

        public static List<ContainerProjectItem> ToProjectItems(ApiContainerPackingProjectSaveRequest request)
        {
            return (request?.CargoItems ?? new List<ApiContainerPackingCargoInputDto>())
                .Where(item => item != null)
                .Select(ToProjectItem)
                .ToList();
        }

        public static ApiContainerTypeDto FromContainerType(ContainerTypeDefinition type)
        {
            ArgumentNullException.ThrowIfNull(type);

            return new ApiContainerTypeDto
            {
                Id = type.Id,
                Name = type.Name ?? string.Empty,
                Length = type.Length,
                Width = type.Width,
                Height = type.Height,
                MaxVolume = type.MaxVolume,
                MaxWeight = type.MaxWeight,
                IsSystemDefault = type.IsSystemDefault
            };
        }

        public static ContainerTypeDefinition ToContainerType(ApiContainerTypeSaveRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new ContainerTypeDefinition
            {
                Id = Math.Max(request.Id, 0),
                Name = NormalizeName(request.Name, "未命名柜型"),
                Length = Math.Max(request.Length, 0),
                Width = Math.Max(request.Width, 0),
                Height = Math.Max(request.Height, 0),
                MaxVolume = Math.Max(request.MaxVolume, 0m),
                MaxWeight = Math.Max(request.MaxWeight, 0m),
                IsSystemDefault = false
            };
        }

        private static ApiContainerPackingCargoInputDto FromProjectItem(ContainerProjectItem item)
        {
            return new ApiContainerPackingCargoInputDto
            {
                Name = item.Name ?? string.Empty,
                Length = item.Length,
                Width = item.Width,
                Height = item.Height,
                Weight = item.Weight,
                Quantity = item.Quantity,
                ColorArgb = item.ColorArgb,
                UsePallet = item.UsePallet,
                UnitsPerPallet = item.UnitsPerPallet,
                MaxTopLoadWeight = item.MaxTopLoadWeight,
                PreferredZone = item.PreferredZone ?? nameof(ContainerCargoZone.Auto),
                LoadSequence = item.LoadSequence,
                PriorityGroup = item.PriorityGroup ?? string.Empty
            };
        }

        private static ContainerProjectItem ToProjectItem(ApiContainerPackingCargoInputDto item)
        {
            return new ContainerProjectItem
            {
                Name = NormalizeName(item.Name, "货物"),
                Length = Math.Max(item.Length, 0m),
                Width = Math.Max(item.Width, 0m),
                Height = Math.Max(item.Height, 0m),
                Weight = Math.Max(item.Weight, 0m),
                Quantity = Math.Max(item.Quantity, 0),
                ColorArgb = item.ColorArgb,
                UsePallet = item.UsePallet,
                UnitsPerPallet = Math.Max(item.UnitsPerPallet, 1),
                MaxTopLoadWeight = Math.Max(item.MaxTopLoadWeight, 0m),
                PreferredZone = NormalizeZone(item.PreferredZone),
                LoadSequence = Math.Max(item.LoadSequence, 1),
                PriorityGroup = item.PriorityGroup?.Trim() ?? string.Empty
            };
        }

        private static string NormalizeName(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeZone(string preferredZone)
        {
            return Enum.TryParse<ContainerCargoZone>(preferredZone, ignoreCase: true, out var zone) && Enum.IsDefined(zone)
                ? zone.ToString()
                : ContainerCargoZone.Auto.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ExportDocManager.Models;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Tools
{
    public class ContainerLoadingService : IContainerLoadingService
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;

        public ContainerLoadingService(IDbContextFactory<AppDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<ContainerProject>> GetAllProjectsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ContainerProjects
                .AsNoTracking()
                .OrderByDescending(p => p.UpdatedAt)
                .ToListAsync();
        }

        public async Task<ContainerProject> GetProjectAsync(int projectId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ContainerProjects
                .AsNoTracking()
                .SingleOrDefaultAsync(project => project.Id == projectId);
        }

        public async Task<List<ContainerProjectItem>> GetProjectItemsAsync(int projectId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ContainerProjectItems
                .AsNoTracking()
                .Where(i => i.ProjectId == projectId)
                .OrderBy(i => i.LoadSequence)
                .ThenBy(i => i.Id)
                .ToListAsync();
        }

        public async Task SaveProjectAsync(ContainerProject project, List<ContainerProjectItem> items)
        {
            ArgumentNullException.ThrowIfNull(project);
            ArgumentNullException.ThrowIfNull(items);

            var sanitizedItems = items
                .Where(item => item != null)
                .Select(CreateSanitizedItem)
                .Where(item => item.Quantity > 0 &&
                               item.Length > 0 &&
                               item.Width > 0 &&
                               item.Height > 0)
                .ToList();

            if (sanitizedItems.Count == 0)
            {
                throw new InvalidOperationException("至少需要一个有效货物项才能保存装柜方案。");
            }

            var savedProject = await AppDbContextExecution.ExecuteInTransactionAsync(
                _contextFactory,
                async (context, _) =>
                {
                    var now = DateTime.Now;
                    ContainerProject targetProject;

                    if (project.Id == 0)
                    {
                        targetProject = new ContainerProject
                        {
                            CreatedAt = now
                        };
                        ApplyProjectValues(targetProject, project, now);

                        context.ContainerProjects.Add(targetProject);
                        await context.SaveChangesAsync();
                    }
                    else
                    {
                        targetProject = await context.ContainerProjects
                            .SingleOrDefaultAsync(existingProject => existingProject.Id == project.Id)
                            ?? throw new InvalidOperationException("要保存的装柜方案不存在，可能已被删除。");

                        ApplyProjectValues(targetProject, project, now);

                        await context.ContainerProjectItems
                            .Where(item => item.ProjectId == project.Id)
                            .ExecuteDeleteAsync();
                    }

                    foreach (var item in sanitizedItems)
                    {
                        item.ProjectId = targetProject.Id;
                        context.ContainerProjectItems.Add(item);
                    }

                    await context.SaveChangesAsync();
                    return targetProject;
                });

            CopySavedProject(project, savedProject);
        }

        public async Task DeleteProjectAsync(int projectId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var project = await context.ContainerProjects.FindAsync(projectId);
            if (project != null)
            {
                context.ContainerProjects.Remove(project);
                await context.SaveChangesAsync();
            }
        }

        // --- Container Type Management ---

        public async Task<List<ContainerTypeDefinition>> GetContainerTypesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.ContainerTypeDefinitions
                .AsNoTracking()
                .OrderByDescending(t => t.IsSystemDefault)
                .ThenBy(t => t.Name)
                .ToListAsync();
        }

        public async Task SaveContainerTypeAsync(ContainerTypeDefinition typeDef)
        {
            ArgumentNullException.ThrowIfNull(typeDef);

            using var context = await _contextFactory.CreateDbContextAsync();
            string normalizedName = NormalizeContainerTypeName(typeDef.Name);
            var savedType = await context.ContainerTypeDefinitions
                .FirstOrDefaultAsync(item => item.Id == typeDef.Id);
            if (savedType?.IsSystemDefault == true)
            {
                throw new InvalidOperationException("系统默认柜型不支持覆盖，请换一个名称保存。");
            }

            if (savedType == null)
            {
                var allTypes = await context.ContainerTypeDefinitions.ToListAsync();
                savedType = allTypes
                    .FirstOrDefault(item => string.Equals(
                        NormalizeContainerTypeName(item.Name),
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase));

                if (savedType?.IsSystemDefault == true)
                {
                    throw new InvalidOperationException("系统默认柜型不支持覆盖，请换一个名称保存。");
                }
            }

            if (savedType == null)
            {
                savedType = new ContainerTypeDefinition
                {
                    Name = normalizedName,
                    Length = Math.Max(typeDef.Length, 1),
                    Width = Math.Max(typeDef.Width, 1),
                    Height = Math.Max(typeDef.Height, 1),
                    MaxVolume = Math.Max(typeDef.MaxVolume, 0),
                    MaxWeight = Math.Max(typeDef.MaxWeight, 0),
                    IsSystemDefault = typeDef.IsSystemDefault
                };
                context.ContainerTypeDefinitions.Add(savedType);
            }
            else
            {
                savedType.Name = normalizedName;
                savedType.Length = Math.Max(typeDef.Length, 1);
                savedType.Width = Math.Max(typeDef.Width, 1);
                savedType.Height = Math.Max(typeDef.Height, 1);
                savedType.MaxVolume = Math.Max(typeDef.MaxVolume, 0);
                savedType.MaxWeight = Math.Max(typeDef.MaxWeight, 0);
                savedType.IsSystemDefault = typeDef.IsSystemDefault;
            }

            await context.SaveChangesAsync();

            typeDef.Id = savedType.Id;
            typeDef.Name = normalizedName;
            typeDef.Length = Math.Max(typeDef.Length, 1);
            typeDef.Width = Math.Max(typeDef.Width, 1);
            typeDef.Height = Math.Max(typeDef.Height, 1);
            typeDef.MaxVolume = Math.Max(typeDef.MaxVolume, 0);
            typeDef.MaxWeight = Math.Max(typeDef.MaxWeight, 0);
            typeDef.IsSystemDefault = savedType.IsSystemDefault;
        }

        public async Task DeleteContainerTypeAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var typeDef = await context.ContainerTypeDefinitions.FindAsync(id);
            if (typeDef != null && !typeDef.IsSystemDefault)
            {
                context.ContainerTypeDefinitions.Remove(typeDef);
                await context.SaveChangesAsync();
            }
        }

        private static ContainerProjectItem CreateSanitizedItem(ContainerProjectItem item)
        {
            return new ContainerProjectItem
            {
                Name = string.IsNullOrWhiteSpace(item.Name) ? "货物" : item.Name.Trim(),
                Length = item.Length > 0 ? item.Length : 0,
                Width = item.Width > 0 ? item.Width : 0,
                Height = item.Height > 0 ? item.Height : 0,
                Weight = item.Weight > 0 ? item.Weight : 0,
                Quantity = Math.Max(item.Quantity, 0),
                UsePallet = item.UsePallet,
                UnitsPerPallet = Math.Max(item.UnitsPerPallet, 1),
                MaxTopLoadWeight = Math.Max(item.MaxTopLoadWeight, 0),
                PreferredZone = NormalizePreferredZone(item.PreferredZone),
                LoadSequence = Math.Max(item.LoadSequence, 1),
                PriorityGroup = NormalizePriorityGroup(item.PriorityGroup),
                ColorArgb = item.ColorArgb
            };
        }

        private static void ApplyProjectValues(ContainerProject target, ContainerProject source, DateTime updatedAt)
        {
            target.Name = NormalizeProjectName(source.Name);
            target.ContainerType = NormalizeContainerType(source.ContainerType);
            target.ContainerLength = Math.Max(source.ContainerLength, 1);
            target.ContainerWidth = Math.Max(source.ContainerWidth, 1);
            target.ContainerHeight = Math.Max(source.ContainerHeight, 1);
            target.ContainerMaxVolume = Math.Max(source.ContainerMaxVolume, 0);
            target.ContainerMaxWeight = Math.Max(source.ContainerMaxWeight, 0);
            target.AllowRotation = source.AllowRotation;
            target.UsePalletConstraints = source.UsePalletConstraints;
            target.DefaultPalletLength = Math.Max(source.DefaultPalletLength, 1);
            target.DefaultPalletWidth = Math.Max(source.DefaultPalletWidth, 1);
            target.DefaultPalletHeight = Math.Max(source.DefaultPalletHeight, 0);
            target.DefaultPalletWeight = Math.Max(source.DefaultPalletWeight, 0);
            target.EnforceCenterOfGravity = source.EnforceCenterOfGravity;
            target.CenterOfGravityTolerancePercent = Math.Max(source.CenterOfGravityTolerancePercent, 0);
            target.MinimumSupportAreaPercent = Math.Clamp(source.MinimumSupportAreaPercent, 0, 100);
            target.RequireSameFootprintStacking = source.RequireSameFootprintStacking;
            target.UpdatedAt = updatedAt;
        }

        private static void CopySavedProject(ContainerProject target, ContainerProject source)
        {
            target.Id = source.Id;
            target.Name = source.Name;
            target.ContainerType = source.ContainerType;
            target.ContainerLength = source.ContainerLength;
            target.ContainerWidth = source.ContainerWidth;
            target.ContainerHeight = source.ContainerHeight;
            target.ContainerMaxVolume = source.ContainerMaxVolume;
            target.ContainerMaxWeight = source.ContainerMaxWeight;
            target.AllowRotation = source.AllowRotation;
            target.UsePalletConstraints = source.UsePalletConstraints;
            target.DefaultPalletLength = source.DefaultPalletLength;
            target.DefaultPalletWidth = source.DefaultPalletWidth;
            target.DefaultPalletHeight = source.DefaultPalletHeight;
            target.DefaultPalletWeight = source.DefaultPalletWeight;
            target.EnforceCenterOfGravity = source.EnforceCenterOfGravity;
            target.CenterOfGravityTolerancePercent = source.CenterOfGravityTolerancePercent;
            target.MinimumSupportAreaPercent = source.MinimumSupportAreaPercent;
            target.RequireSameFootprintStacking = source.RequireSameFootprintStacking;
            target.CreatedAt = source.CreatedAt;
            target.UpdatedAt = source.UpdatedAt;
        }

        private static string NormalizeProjectName(string projectName)
        {
            return string.IsNullOrWhiteSpace(projectName) ? "未命名方案" : projectName.Trim();
        }

        private static string NormalizeContainerType(string containerType)
        {
            return string.IsNullOrWhiteSpace(containerType) ? string.Empty : containerType.Trim();
        }

        private static string NormalizeContainerTypeName(string containerTypeName)
        {
            return string.IsNullOrWhiteSpace(containerTypeName) ? "未命名柜型" : containerTypeName.Trim();
        }

        private static string NormalizePreferredZone(string preferredZone)
        {
            return Enum.TryParse<ContainerCargoZone>(preferredZone, ignoreCase: true, out var zone) && Enum.IsDefined(zone)
                ? zone.ToString()
                : ContainerCargoZone.Auto.ToString();
        }

        private static string NormalizePriorityGroup(string priorityGroup)
        {
            return string.IsNullOrWhiteSpace(priorityGroup) ? string.Empty : priorityGroup.Trim();
        }
    }
}

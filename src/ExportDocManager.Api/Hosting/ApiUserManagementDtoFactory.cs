using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiUserManagementDtoFactory
    {
        public static ApiUserAccountDto FromUser(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            return new ApiUserAccountDto(
                user.Id,
                user.Username ?? string.Empty,
                user.FullName ?? string.Empty,
                user.Role ?? string.Empty,
                user.PermissionTemplateId,
                user.PermissionTemplate?.Code ?? string.Empty,
                user.PermissionTemplate?.Name ?? string.Empty,
                user.DepartmentId ?? string.Empty,
                user.CompanyScope ?? string.Empty,
                user.IsActive);
        }

        public static User ToUser(ApiUserSaveRequest request, int id)
        {
            ArgumentNullException.ThrowIfNull(request);

            return new User
            {
                Id = id,
                Username = request.Username ?? string.Empty,
                FullName = request.FullName ?? string.Empty,
                Role = UserRoleCatalog.Normalize(request.Role),
                PermissionTemplateId = request.PermissionTemplateId,
                DepartmentId = request.DepartmentId ?? string.Empty,
                CompanyScope = request.CompanyScope ?? string.Empty,
                IsActive = request.IsActive
            };
        }
    }
}

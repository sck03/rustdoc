using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Time;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiUserDtoFactory
    {
        public static ApiUserDto FromUser(
            User user,
            ApiAuthorizationService authorizationService,
            IBusinessClock clock)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(authorizationService);
            ArgumentNullException.ThrowIfNull(clock);

            return new ApiUserDto(
                user.Id,
                user.Username ?? string.Empty,
                user.FullName ?? string.Empty,
                user.Role ?? string.Empty,
                user.DepartmentId ?? string.Empty,
                user.CompanyScope ?? string.Empty,
                user.IsActive,
                clock.Today,
                clock.TimeZoneId,
                clock.TodayValidUntilUtc,
                authorizationService.GetCapabilities(user));
        }

        public static User ToUserSnapshot(User user)
        {
            ArgumentNullException.ThrowIfNull(user);

            return new User
            {
                Id = user.Id,
                Username = user.Username ?? string.Empty,
                PasswordHash = string.Empty,
                FullName = user.FullName ?? string.Empty,
                Role = user.Role ?? string.Empty,
                PermissionTemplateId = user.PermissionTemplateId,
                EffectiveModuleAccess = new Dictionary<string, string>(
                    user.EffectiveModuleAccess ?? new Dictionary<string, string>(),
                    StringComparer.OrdinalIgnoreCase),
                DepartmentId = user.DepartmentId ?? string.Empty,
                CompanyScope = user.CompanyScope ?? string.Empty,
                IsActive = user.IsActive
            };
        }
    }
}

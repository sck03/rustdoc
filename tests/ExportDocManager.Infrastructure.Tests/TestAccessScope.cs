using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.Infrastructure.Tests;

internal static class TestAccessScope
{
    public static BusinessDataAccessScope Create(
        DatabaseConnectionSettings? settings = null,
        ICurrentUserContext? currentUserContext = null) =>
        new(settings ?? new DatabaseConnectionSettings(), currentUserContext);
}

internal sealed class FixedCurrentUserContext(User user) : ICurrentUserContext
{
    public User CurrentUser { get; } = user;
}

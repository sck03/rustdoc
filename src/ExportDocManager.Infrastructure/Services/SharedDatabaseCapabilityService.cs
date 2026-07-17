using ExportDocManager.DataAccess;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Infrastructure
{
    public sealed class SharedDatabaseCapabilityService : ISharedDatabaseCapabilityService
    {
        private readonly DatabaseConnectionSettings _databaseSettings;

        public SharedDatabaseCapabilityService(DatabaseConnectionSettings databaseSettings)
        {
            _databaseSettings = databaseSettings ?? throw new ArgumentNullException(nameof(databaseSettings));
        }

        public SharedDatabaseCapabilityProfile GetCurrentProfile()
        {
            bool postgreSqlSelected = DatabaseModeHelper.UsesPostgreSql(_databaseSettings);
            bool sharedEnabled = DatabaseModeHelper.UsesSharedDatabase(_databaseSettings);
            return new SharedDatabaseCapabilityProfile
            {
                SharedDatabaseEnabled = sharedEnabled,
                SharedDatabasePendingConfiguration = postgreSqlSelected && !sharedEnabled,
                CurrentModeText = DatabaseModeHelper.GetCurrentModeText(_databaseSettings),
                PlannedModules = sharedEnabled
                    ? ["单一窗口协同工单自动分派"]
                    : []
            };
        }
    }
}

using ExportDocManager.Models;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class PostgreSqlAutomaticBackupHostedServiceTests
    {
        [Fact]
        public void WeeklySchedule_ShouldCatchUpAfterMissedScheduledDay()
        {
            var settings = new SystemSettings
            {
                PostgreSqlAutoBackupSchedule = "Weekly",
                PostgreSqlAutoBackupDayOfWeek = (int)DayOfWeek.Monday,
                PostgreSqlAutoBackupTime = "02:00"
            };
            var now = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.FromHours(8));

            Assert.True(PostgreSqlAutomaticBackupHostedService.ShouldRun(
                settings,
                now,
                new PostgreSqlAutomaticBackupHostedService.PostgreSqlAutomaticBackupState()));

            Assert.False(PostgreSqlAutomaticBackupHostedService.ShouldRun(
                settings,
                now,
                new PostgreSqlAutomaticBackupHostedService.PostgreSqlAutomaticBackupState
                {
                    LastSuccessfulRunAt = new DateTimeOffset(2026, 7, 27, 2, 5, 0, TimeSpan.FromHours(8))
                }));
        }

        [Fact]
        public void WeeklySchedule_ShouldNotRunBeforeCurrentWeeksScheduledOccurrence()
        {
            var settings = new SystemSettings
            {
                PostgreSqlAutoBackupSchedule = "Weekly",
                PostgreSqlAutoBackupDayOfWeek = (int)DayOfWeek.Friday,
                PostgreSqlAutoBackupTime = "18:30"
            };
            var now = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.FromHours(8));

            Assert.False(PostgreSqlAutomaticBackupHostedService.ShouldRun(
                settings,
                now,
                new PostgreSqlAutomaticBackupHostedService.PostgreSqlAutomaticBackupState()));
        }

        [Fact]
        public void DailySchedule_ShouldRunOnlyOnceAfterScheduledTime()
        {
            var settings = new SystemSettings
            {
                PostgreSqlAutoBackupSchedule = "Daily",
                PostgreSqlAutoBackupTime = "02:00"
            };
            var now = new DateTimeOffset(2026, 7, 29, 3, 0, 0, TimeSpan.FromHours(8));

            Assert.True(PostgreSqlAutomaticBackupHostedService.ShouldRun(
                settings,
                now,
                new PostgreSqlAutomaticBackupHostedService.PostgreSqlAutomaticBackupState()));
            Assert.False(PostgreSqlAutomaticBackupHostedService.ShouldRun(
                settings,
                now,
                new PostgreSqlAutomaticBackupHostedService.PostgreSqlAutomaticBackupState
                {
                    LastSuccessfulRunAt = new DateTimeOffset(2026, 7, 29, 2, 1, 0, TimeSpan.FromHours(8))
                }));
        }
    }
}

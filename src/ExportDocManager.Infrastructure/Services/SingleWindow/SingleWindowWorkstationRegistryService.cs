using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.SingleWindow
{
    public sealed class SingleWindowWorkstationRegistryService : ISingleWindowWorkstationRegistryService
    {
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly ICurrentUserContext _currentUserContext;

        public SingleWindowWorkstationRegistryService(
            IDbContextFactory<AppDbContext> contextFactory,
            ICurrentUserContext currentUserContext = null)
        {
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _currentUserContext = currentUserContext;
        }

        public async Task EnsureCurrentWorkstationAsync(CancellationToken cancellationToken = default)
        {
            string machineName = (Environment.MachineName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(machineName))
            {
                return;
            }

            using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var profile = await context.SwClientProfiles
                .AsNoTracking()
                .Where(item => item.IsEnabled && item.MachineName == machineName)
                .OrderByDescending(item => item.UpdatedAt)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var workstation = await context.SwOperatorWorkstations
                .FirstOrDefaultAsync(item => item.MachineName == machineName, cancellationToken);

            bool changed = false;
            if (workstation == null)
            {
                workstation = new SwOperatorWorkstation
                {
                    MachineName = machineName
                };
                await context.SwOperatorWorkstations.AddAsync(workstation, cancellationToken);
                changed = true;
            }

            string operatorName = ResolveCurrentOperatorName();
            if (!string.IsNullOrWhiteSpace(operatorName) &&
                !string.Equals(workstation.OperatorName, operatorName, StringComparison.Ordinal))
            {
                workstation.OperatorName = operatorName;
                changed = true;
            }

            if (profile != null)
            {
                if (workstation.ProfileId != profile.Id)
                {
                    workstation.ProfileId = profile.Id;
                    changed = true;
                }

                if (workstation.CanSubmitAgentConsignment != profile.CanSubmitAgentConsignment)
                {
                    workstation.CanSubmitAgentConsignment = profile.CanSubmitAgentConsignment;
                    changed = true;
                }

                if (workstation.CanSubmitCustomsCoo != profile.CanSubmitCustomsCoo)
                {
                    workstation.CanSubmitCustomsCoo = profile.CanSubmitCustomsCoo;
                    changed = true;
                }

                if (workstation.IsEnabled != profile.IsEnabled)
                {
                    workstation.IsEnabled = profile.IsEnabled;
                    changed = true;
                }
            }
            else
            {
                if (workstation.ProfileId != null)
                {
                    workstation.ProfileId = null;
                    changed = true;
                }

                if (workstation.CanSubmitAgentConsignment)
                {
                    workstation.CanSubmitAgentConsignment = false;
                    changed = true;
                }

                if (workstation.CanSubmitCustomsCoo)
                {
                    workstation.CanSubmitCustomsCoo = false;
                    changed = true;
                }

                if (!workstation.IsEnabled)
                {
                    workstation.IsEnabled = true;
                    changed = true;
                }
            }

            if (!changed &&
                workstation.UpdatedAt >= DateTime.Now.Subtract(HeartbeatInterval))
            {
                return;
            }

            workstation.UpdatedAt = DateTime.Now;
            await context.SaveChangesAsync(cancellationToken);
        }

        private string ResolveCurrentOperatorName()
        {
            var currentUser = _currentUserContext?.CurrentUser;
            return (currentUser?.FullName ?? currentUser?.Username ?? Environment.UserName ?? string.Empty).Trim();
        }
    }
}

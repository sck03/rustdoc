using System.Threading.Tasks;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Services.Security
{
    public interface IUserService
    {
        Task<User> AuthenticateAsync(string username, string password);
        Task<User> GetUserByUsernameAsync(string username);
        Task<User> GetActiveUserByIdAsync(int userId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<User>> GetUsersAsync(CancellationToken cancellationToken = default);
        Task<int> SaveUserAsync(User user, string resetPassword = "", CancellationToken cancellationToken = default);
        Task<bool> DeleteUserAsync(int userId, CancellationToken cancellationToken = default);
    }
}

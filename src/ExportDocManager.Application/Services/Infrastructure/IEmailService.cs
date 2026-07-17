using System.Collections.Generic;
using System.Threading.Tasks;
using ExportDocManager.Models;

namespace ExportDocManager.Services.Infrastructure
{
    public interface IEmailService
    {
        /// <summary>
        /// Sends an email with attachments.
        /// </summary>
        /// <param name="to">Recipient email address</param>
        /// <param name="subject">Email subject</param>
        /// <param name="body">Email body content</param>
        /// <param name="attachments">List of file paths to attach</param>
        Task SendEmailAsync(string to, string subject, string body, List<string> attachments = null);

        /// <summary>
        /// Tests the connection with current configuration.
        /// </summary>
        Task TestConnectionAsync(EmailConfig config);
    }
}

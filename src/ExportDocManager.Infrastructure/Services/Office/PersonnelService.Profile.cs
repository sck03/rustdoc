using System.Globalization;
using System.Net.Mail;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using static ExportDocManager.Services.Office.OfficeServiceContext;

namespace ExportDocManager.Services.Office;

public sealed partial class PersonnelService
{
    private static PersonnelProfile Profile(PersonnelEmployee employee) => new(employee.FullName, employee.WorkEmail,
        employee.WorkPhone, employee.WorkLocation, employee.PersonalPhone, employee.EmergencyContact, employee.EmergencyPhone, employee.Notes,
        employee.IdentityNumber, employee.IdentityAuthority, employee.RegisteredAddress, employee.IdentityValidFrom,
        employee.IdentityValidUntil, employee.IdentityLongTerm);

    private void ApplyProfile(PersonnelEmployee employee, PersonnelProfile profile, EmploymentType type,
        DateOnly? probationEnd, DateOnly? contractEnd)
    {
        if (profile == null) throw new ServiceValidationException("人员信息不能为空。");
        if (!Enum.IsDefined(type)) throw new ServiceValidationException("用工类型无效。");
        employee.FullName = Text(profile.FullName, "姓名", 100, true);
        employee.WorkEmail = Text(profile.WorkEmail, "工作邮箱", 254);
        if (employee.WorkEmail.Length > 0 && (!MailAddress.TryCreate(employee.WorkEmail, out var address) || address.Address != employee.WorkEmail))
            throw new ServiceValidationException("请填写有效的工作邮箱地址。");
        employee.WorkPhone = Text(profile.WorkPhone, "工作电话", 50);
        employee.WorkLocation = Text(profile.WorkLocation, "工作地点", 120);
        employee.PersonalPhone = Text(profile.PersonalPhone, "个人电话", 50);
        employee.EmergencyContact = Text(profile.EmergencyContact, "紧急联系人", 100);
        employee.EmergencyPhone = Text(profile.EmergencyPhone, "紧急联系电话", 50);
        if ((employee.EmergencyContact.Length == 0) != (employee.EmergencyPhone.Length == 0))
            throw new ServiceValidationException("紧急联系人与联系电话须一起填写。");
        employee.Notes = Text(profile.Notes, "人事备注", 1000);
        ApplyIdentity(employee, profile);
        if (probationEnd < employee.HireDate || contractEnd < employee.HireDate || probationEnd.HasValue && contractEnd < probationEnd)
            throw new ServiceValidationException("试用和合同截止日期不能早于入职，试用期不能超过合同期限。");
        employee.EmploymentType = type;
        employee.ProbationEndsOn = probationEnd;
        employee.ContractEndsOn = contractEnd;
    }

    private void ApplyIdentity(PersonnelEmployee employee, PersonnelProfile profile)
    {
        string number = Text(profile.IdentityNumber, "居民身份证号码", 18).ToUpperInvariant();
        DateOnly? birthday = null;
        if (number.Length > 0)
        {
            if (number.Length != 18 || number[..17].Any(ch => ch is < '0' or > '9') || number[0] == '0' ||
                number.Substring(14, 3) == "000" || !DateOnly.TryParseExact(number.Substring(6, 8), "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var born) || born > office.Clock.Today || born.Year < 1900)
                throw new ServiceValidationException("请填写有效的 18 位居民身份证号码。");
            int[] weights = [7, 9, 10, 5, 8, 4, 2, 1, 6, 3, 7, 9, 10, 5, 8, 4, 2];
            int sum = weights.Select((weight, index) => weight * (number[index] - '0')).Sum();
            if (number[17] != "10X98765432"[sum % 11])
                throw new ServiceValidationException("身份证号码校验位不正确，请核对原件。");
            birthday = born;
        }
        bool hasValidity = profile.IdentityValidFrom.HasValue || profile.IdentityValidUntil.HasValue || profile.IdentityLongTerm;
        if (hasValidity && (number.Length == 0 || !profile.IdentityValidFrom.HasValue ||
            profile.IdentityValidFrom < new DateOnly(1900, 1, 1) || profile.IdentityValidFrom < birthday ||
            profile.IdentityValidFrom > office.Clock.Today ||
            (profile.IdentityLongTerm ? profile.IdentityValidUntil.HasValue : !profile.IdentityValidUntil.HasValue || profile.IdentityValidUntil < profile.IdentityValidFrom)))
            throw new ServiceValidationException("身份证有效期须填写号码和有效起始日，并选择长期有效或不早于起始日的截止日。");
        employee.IdentityNumber = number;
        employee.IdentityAuthority = Text(profile.IdentityAuthority, "签发机关", 120);
        employee.RegisteredAddress = Text(profile.RegisteredAddress, "身份证住址", 300);
        employee.IdentityValidFrom = profile.IdentityValidFrom;
        employee.IdentityValidUntil = profile.IdentityValidUntil;
        employee.IdentityLongTerm = profile.IdentityLongTerm;
    }

    private static string ProfileChanges(PersonnelProfile before, PersonnelProfile after)
    {
        (string Name, object? Before, object? After)[] fields = [
            ("姓名", before.FullName, after.FullName), ("工作邮箱", before.WorkEmail, after.WorkEmail),
            ("工作电话", before.WorkPhone, after.WorkPhone), ("工作地点", before.WorkLocation, after.WorkLocation),
            ("个人电话", before.PersonalPhone, after.PersonalPhone), ("紧急联系人", before.EmergencyContact, after.EmergencyContact),
            ("紧急联系电话", before.EmergencyPhone, after.EmergencyPhone), ("人事备注", before.Notes, after.Notes),
            ("身份证号码", before.IdentityNumber, after.IdentityNumber), ("签发机关", before.IdentityAuthority, after.IdentityAuthority),
            ("身份证住址", before.RegisteredAddress, after.RegisteredAddress), ("证件有效起始日", before.IdentityValidFrom, after.IdentityValidFrom),
            ("证件有效截止日", before.IdentityValidUntil, after.IdentityValidUntil), ("证件长期有效", before.IdentityLongTerm, after.IdentityLongTerm)];
        return string.Concat(fields.Where(field => !Equals(field.Before, field.After)).Select(field => field.Name + "；"));
    }
}

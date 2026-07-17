using ExportDocManager.Models;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Security;

namespace ExportDocManager.DataAccess
{
    public static class DbSeeder
    {
        public static void SeedAuxiliaryData(
            AppDbContext context,
            DatabaseConnectionSettings databaseSettings = null,
            string initialAdminPassword = null)
        {
            ArgumentNullException.ThrowIfNull(context);
            bool usesPostgreSql = databaseSettings != null &&
                                  DatabaseModeHelper.UsesPostgreSql(databaseSettings);

            SeedPermissionTemplates(context);
            int adminTemplateId = context.PermissionTemplates
                .Where(template => template.Code == BuiltInPermissionTemplateCatalog.Admin)
                .Select(template => template.Id)
                .Single();

            if (!context.Users.Any())
            {
                if (usesPostgreSql && string.IsNullOrWhiteSpace(initialAdminPassword))
                {
                    throw new InvalidOperationException(
                        "共享数据库首次初始化只能使用 admin 账号登录，并设置至少 8 个字符的初始密码。");
                }

                if (usesPostgreSql)
                {
                    UserPasswordPolicy.EnsureValid(initialAdminPassword, "admin 初始密码");
                }

                var admin = new User
                {
                    Username = "admin",
                    PasswordHash = PasswordHasher.HashPassword(usesPostgreSql ? initialAdminPassword : string.Empty),
                    FullName = "System Administrator",
                    Role = UserRoleCatalog.Admin,
                    PermissionTemplateId = adminTemplateId,
                    IsActive = true
                };
                context.Users.Add(admin);
                context.SaveChanges();
            }

            if (!context.Units.Any())
            {
                var units = new[]
                {
                    // Split PCS into multiple common units for smart mapping
                    new Unit { NameEN = "PCS", NameCN = "只", Code = "01" },
                    new Unit { NameEN = "PCS", NameCN = "件", Code = "01" },
                    new Unit { NameEN = "PCS", NameCN = "支", Code = "01" },
                    new Unit { NameEN = "PCS", NameCN = "条", Code = "01" },
                    
                    new Unit { NameEN = "CTNS", NameCN = "箱", Code = "02" },
                    new Unit { NameEN = "KGS", NameCN = "千克", Code = "03" },
                    new Unit { NameEN = "SETS", NameCN = "套", Code = "04" },
                    new Unit { NameEN = "M", NameCN = "米", Code = "05" },
                    new Unit { NameEN = "M2", NameCN = "平方米", Code = "06" },
                    new Unit { NameEN = "M3", NameCN = "立方米", Code = "07" },
                    new Unit { NameEN = "DOZ", NameCN = "打", Code = "08" },
                    new Unit { NameEN = "PAIRS", NameCN = "双", Code = "09" },
                    new Unit { NameEN = "ROLLS", NameCN = "卷", Code = "10" },
                    new Unit { NameEN = "BAGS", NameCN = "袋", Code = "11" },
                    new Unit { NameEN = "PALLETS", NameCN = "托盘", Code = "12" },
                    new Unit { NameEN = "BOX", NameCN = "盒", Code = "13" },
                    new Unit { NameEN = "GROSS", NameCN = "罗", Code = "14" },
                    new Unit { NameEN = "TONS", NameCN = "吨", Code = "15" }
                };
                context.Units.AddRange(units);
            }

            if (!context.Ports.Any())
            {
                var ports = new[]
                {
                    // China
                    new Port { NameEN = "SHANGHAI", NameCN = "上海", Country = "CHINA", Code = "CNSHA" },
                    new Port { NameEN = "NINGBO", NameCN = "宁波", Country = "CHINA", Code = "CNNGB" },
                    new Port { NameEN = "SHENZHEN", NameCN = "深圳", Country = "CHINA", Code = "CNSZX" },
                    new Port { NameEN = "GUANGZHOU", NameCN = "广州", Country = "CHINA", Code = "CNGZG" },
                    new Port { NameEN = "QINGDAO", NameCN = "青岛", Country = "CHINA", Code = "CNTAO" },
                    new Port { NameEN = "TIANJIN", NameCN = "天津", Country = "CHINA", Code = "CNTSN" },
                    new Port { NameEN = "XIAMEN", NameCN = "厦门", Country = "CHINA", Code = "CNXMN" },
                    new Port { NameEN = "DALIAN", NameCN = "大连", Country = "CHINA", Code = "CNDLC" },
                    new Port { NameEN = "HONG KONG", NameCN = "香港", Country = "CHINA", Code = "HKHKG" },

                    // Asia
                    new Port { NameEN = "SINGAPORE", NameCN = "新加坡", Country = "SINGAPORE", Code = "SGSIN" },
                    new Port { NameEN = "BUSAN", NameCN = "釜山", Country = "KOREA", Code = "KRPUS" },
                    new Port { NameEN = "TOKYO", NameCN = "东京", Country = "JAPAN", Code = "JPTYO" },
                    new Port { NameEN = "YOKOHAMA", NameCN = "横滨", Country = "JAPAN", Code = "JPYOK" },
                    new Port { NameEN = "OSAKA", NameCN = "大阪", Country = "JAPAN", Code = "JPOSA" },
                    new Port { NameEN = "PORT KELANG", NameCN = "巴生港", Country = "MALAYSIA", Code = "MYPKG" },
                    new Port { NameEN = "JAKARTA", NameCN = "雅加达", Country = "INDONESIA", Code = "IDJKT" },
                    new Port { NameEN = "BANGKOK", NameCN = "曼谷", Country = "THAILAND", Code = "THBKK" },
                    new Port { NameEN = "HO CHI MINH", NameCN = "胡志明", Country = "VIETNAM", Code = "VNSGN" },
                    new Port { NameEN = "MANILA", NameCN = "马尼拉", Country = "PHILIPPINES", Code = "PHMNL" },
                    new Port { NameEN = "DUBAI", NameCN = "迪拜", Country = "UAE", Code = "AEDXB" },
                    new Port { NameEN = "JEBEL ALI", NameCN = "杰贝阿里", Country = "UAE", Code = "AEJEA" },

                    // Europe
                    new Port { NameEN = "ROTTERDAM", NameCN = "鹿特丹", Country = "NETHERLANDS", Code = "NLRTM" },
                    new Port { NameEN = "ANTWERP", NameCN = "安特卫普", Country = "BELGIUM", Code = "BEANR" },
                    new Port { NameEN = "HAMBURG", NameCN = "汉堡", Country = "GERMANY", Code = "DEHAM" },
                    new Port { NameEN = "FELIXSTOWE", NameCN = "费利克斯托", Country = "UK", Code = "GBFXT" },
                    new Port { NameEN = "SOUTHAMPTON", NameCN = "南安普顿", Country = "UK", Code = "GBSOU" },
                    new Port { NameEN = "LE HAVRE", NameCN = "勒阿弗尔", Country = "FRANCE", Code = "FRLEH" },
                    new Port { NameEN = "BARCELONA", NameCN = "巴塞罗那", Country = "SPAIN", Code = "ESBCN" },
                    new Port { NameEN = "VALENCIA", NameCN = "瓦伦西亚", Country = "SPAIN", Code = "ESVLC" },
                    new Port { NameEN = "GENOA", NameCN = "热那亚", Country = "ITALY", Code = "ITGOA" },

                    // North America
                    new Port { NameEN = "LOS ANGELES", NameCN = "洛杉矶", Country = "USA", Code = "USLAX" },
                    new Port { NameEN = "LONG BEACH", NameCN = "长滩", Country = "USA", Code = "USLGB" },
                    new Port { NameEN = "NEW YORK", NameCN = "纽约", Country = "USA", Code = "USNYC" },
                    new Port { NameEN = "SAVANNAH", NameCN = "萨凡纳", Country = "USA", Code = "USSAV" },
                    new Port { NameEN = "VANCOUVER", NameCN = "温哥华", Country = "CANADA", Code = "CAVAN" },
                    new Port { NameEN = "TORONTO", NameCN = "多伦多", Country = "CANADA", Code = "CATOR" },
                    new Port { NameEN = "MONTREAL", NameCN = "蒙特利尔", Country = "CANADA", Code = "CAMTR" },

                    // South America
                    new Port { NameEN = "SANTOS", NameCN = "桑托斯", Country = "BRAZIL", Code = "BRSSZ" },
                    new Port { NameEN = "COLON", NameCN = "科隆", Country = "PANAMA", Code = "PAONX" },
                    new Port { NameEN = "BALBOA", NameCN = "巴尔博亚", Country = "PANAMA", Code = "PABLB" },
                    new Port { NameEN = "BUENOS AIRES", NameCN = "布宜诺斯艾利斯", Country = "ARGENTINA", Code = "ARBUE" },

                    // Oceania
                    new Port { NameEN = "SYDNEY", NameCN = "悉尼", Country = "AUSTRALIA", Code = "AUSYD" },
                    new Port { NameEN = "MELBOURNE", NameCN = "墨尔本", Country = "AUSTRALIA", Code = "AUMEL" },
                    new Port { NameEN = "AUCKLAND", NameCN = "奥克兰", Country = "NEW ZEALAND", Code = "NZAKL" },

                    // Africa
                    new Port { NameEN = "DURBAN", NameCN = "德班", Country = "SOUTH AFRICA", Code = "ZADUR" },
                    new Port { NameEN = "CAPE TOWN", NameCN = "开普敦", Country = "SOUTH AFRICA", Code = "ZACPT" },
                    new Port { NameEN = "ALEXANDRIA", NameCN = "亚历山大", Country = "EGYPT", Code = "EGALY" },
                };
                context.Ports.AddRange(ports);
            }

            if (!context.ContainerTypeDefinitions.Any())
            {
                var containerTypes = new[]
                {
                    new ContainerTypeDefinition 
                    { 
                        Name = "20GP", 
                        Length = 589, Width = 235, Height = 239, 
                        MaxVolume = 28m, // Conservative practical volume
                        MaxWeight = 21000m, // Conservative weight
                        IsSystemDefault = true 
                    },
                    new ContainerTypeDefinition 
                    { 
                        Name = "40GP", 
                        Length = 1203, Width = 235, Height = 239, 
                        MaxVolume = 58m, 
                        MaxWeight = 26000m, 
                        IsSystemDefault = true 
                    },
                    new ContainerTypeDefinition 
                    { 
                        Name = "40HQ", 
                        Length = 1203, Width = 235, Height = 269, 
                        MaxVolume = 68m, 
                        MaxWeight = 26000m, 
                        IsSystemDefault = true 
                    }
                };
                context.ContainerTypeDefinitions.AddRange(containerTypes);
            }

            context.SaveChanges();
        }

        private static void SeedPermissionTemplates(AppDbContext context)
        {
            if (context.PermissionTemplates.Any())
            {
                return;
            }

            foreach (var definition in BuiltInPermissionTemplateCatalog.Templates)
            {
                context.PermissionTemplates.Add(new PermissionTemplate
                {
                    Code = definition.Code,
                    Name = definition.Name,
                    Description = definition.Description,
                    IsSystem = true,
                    IsActive = true,
                    UpdatedAt = DateTime.UtcNow,
                    Modules = definition.GetModuleAccess()
                        .Select(grant => new PermissionTemplateModule
                        {
                            ModuleKey = grant.Key,
                            AccessLevel = grant.Value
                        })
                        .ToList()
                });
            }

            context.SaveChanges();
        }
    }
}

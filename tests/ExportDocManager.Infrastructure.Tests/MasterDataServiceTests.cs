using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Infrastructure;
using ExportDocManager.Services.MasterData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ClosedXML.Excel;

namespace ExportDocManager.Infrastructure.Tests
{
    public class MasterDataServiceTests
    {
        [Fact]
        public async Task HsCodeReferenceSave_ShouldNotDowngradeOrOverwriteActiveAnnualTariff()
        {
            using var factory = new SqliteTestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new HsCodeService(factory, repository);
            await service.SaveAsync(new HsCode
            {
                Code = "6109100000",
                Name = "年度税则名称",
                Elements = "年度申报要素",
                Status = "Active",
                SourceName = "2026年度税则",
                EffectiveYear = 2026
            });

            await service.SaveAsync(new HsCode
            {
                Code = "6109100000",
                Name = "第三方网页名称",
                Elements = "网页申报要素",
                Status = "ReferenceOnly",
                SourceName = "i5a6（第三方参考）"
            });

            var saved = await service.GetByCodeAsync("6109100000");
            Assert.Equal("Active", saved.Status);
            Assert.Equal("年度税则名称", saved.Name);
            Assert.Equal("年度申报要素", saved.Elements);
            Assert.Equal("2026年度税则", saved.SourceName);
            Assert.Equal(2026, saved.EffectiveYear);
        }

        [Fact]
        public async Task HsCodeNumericKeyword_ShouldMatchOnlyCodePrefix()
        {
            using var factory = new SqliteTestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new HsCodeService(factory, repository);
            await service.SaveAsync(new HsCode { Code = "6109100000", Name = "棉制针织T恤衫" });
            await service.SaveAsync(new HsCode { Code = "2846109010", Name = "其他稀土化合物" });

            var result = await service.GetPagedLocalAsync(1, 50, "6109");

            var item = Assert.Single(result.Items);
            Assert.Equal("6109100000", item.Code);
        }

        [Fact]
        public async Task HsCodeImport_ShouldDetectNonStandardHeaderAndPreserveExistingNonEmptyFields()
        {
            using var factory = new SqliteTestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new HsCodeService(factory, repository);
            await service.SaveAsync(new HsCode
            {
                Code = "6205200090",
                Name = "旧名称",
                Elements = "保留的申报要素",
                Unit = "件"
            });
            string path = CreateHsCodeWorkbook(workbook =>
            {
                var sheet = workbook.AddWorksheet("2026税则资料");
                sheet.Cell(1, 1).Value = "某第三方中国HS编码资料";
                sheet.Cell(3, 2).Value = "商品税号";
                sheet.Cell(3, 4).Value = "货品名称";
                sheet.Cell(3, 6).Value = "第一法定单位";
                sheet.Cell(3, 8).Value = "出口商品退税率";
                sheet.Cell(4, 2).Value = "6205200090";
                sheet.Cell(4, 4).Value = "棉制男衬衫";
                sheet.Cell(4, 6).Value = "011 件";
                sheet.Cell(4, 8).Value = "13%";
            });
            try
            {
                var preview = await service.PreviewImportAsync(path, HsCodeImportMode.Incremental, "测试资料", 2026);
                Assert.Equal(3, preview.HeaderRowNumber);
                Assert.True(preview.Confidence >= 75);
                Assert.Equal(1, preview.UpdateCount);

                await service.CommitImportAsync(preview);
                var saved = await service.GetByCodeAsync("6205200090");
                Assert.Equal("棉制男衬衫", saved.Name);
                Assert.Equal("保留的申报要素", saved.Elements);
                Assert.Equal("件", saved.Unit);
                Assert.Equal("13%", saved.RebateRate);
                Assert.Equal("测试资料", saved.SourceName);
                Assert.Equal(2026, saved.EffectiveYear);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task HsCodeCompleteSnapshot_ShouldMarkMissingCodeWithoutDeletingIt()
        {
            using var factory = new SqliteTestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new HsCodeService(factory, repository);
            await service.SaveAsync(new HsCode { Code = "8517000000", Name = "旧通信设备", Unit = "台" });
            string path = CreateHsCodeWorkbook(workbook =>
            {
                var sheet = workbook.AddWorksheet("完整库");
                sheet.Cell(1, 1).Value = "HS编码";
                sheet.Cell(1, 2).Value = "商品名称";
                sheet.Cell(2, 1).Value = "8517130000";
                sheet.Cell(2, 2).Value = "智能手机";
            });
            try
            {
                var preview = await service.PreviewImportAsync(path, HsCodeImportMode.CompleteSnapshot, "完整库", 2026);
                Assert.Equal(1, preview.SuspectedObsoleteCount);
                await service.CommitImportAsync(preview);
                var oldItem = await service.GetByCodeAsync("8517000000");
                Assert.NotNull(oldItem);
                Assert.Equal("SuspectedObsolete", oldItem.Status);
                Assert.NotNull(await service.GetByCodeAsync("8517130000"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task HsCodeImport_ShouldRecognizeSpecificationElementsAndChineseTariffColumns()
        {
            using var factory = new SqliteTestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new HsCodeService(factory, repository);
            string path = CreateHsCodeWorkbook(workbook =>
            {
                var sheet = workbook.AddWorksheet("完整税则");
                string[] headers =
                [
                    "商品编码", "名称", "许可证代码", "普通税率", "优惠税率", "备注",
                    "出口税率", "消费税率", "增值税率", "第一法定单位", "第二法定单位", "规格型号"
                ];
                for (int index = 0; index < headers.Length; index++) sheet.Cell(3, index + 1).Value = headers[index];
                sheet.Cell(4, 1).Value = "0101290010";
                sheet.Cell(4, 2).Value = "非改良种用濒危野马";
                sheet.Cell(4, 3).Value = "AFEB";
                sheet.Cell(4, 4).Value = 0.30m;
                sheet.Cell(4, 5).Value = 0.10m;
                sheet.Cell(4, 6).Value = "普通税率:0.3;优惠税率:0.1;消费税率:0;备注:";
                sheet.Cell(4, 7).Value = 0m;
                sheet.Cell(4, 8).Value = 0m;
                sheet.Cell(4, 9).Value = 0.09m;
                sheet.Cell(4, 10).Value = "035";
                sheet.Cell(4, 11).Value = "009";
                sheet.Cell(4, 12).Value = "0:品牌类型|1:出口享惠情况|2:品种|3:其他";
            });
            try
            {
                var preview = await service.PreviewImportAsync(path);
                var item = Assert.Single(preview.Items).Item;
                Assert.Equal("AFEB", item.SupervisionConditions);
                Assert.Equal("0:品牌类型|1:出口享惠情况|2:品种|3:其他", item.Elements);
                Assert.Equal("30%", item.NormalTariffRate);
                Assert.Equal("10%", item.PreferentialTariffRate);
                Assert.Equal("0%", item.ExportTariffRate);
                Assert.Equal("0%", item.ConsumptionTaxRate);
                Assert.Equal("9%", item.ValueAddedTaxRate);
                Assert.True(string.IsNullOrWhiteSpace(item.Description));
                Assert.True(string.IsNullOrWhiteSpace(item.Notes));
                Assert.Contains(preview.Columns, column => column.Field == "Name" && column.ColumnNumber == 2);
                Assert.DoesNotContain(preview.Columns, column => column.Field == "Description" && column.ColumnNumber == 2);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string CreateHsCodeWorkbook(Action<XLWorkbook> build)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "HsCodeImportTests");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, $"{Guid.NewGuid():N}.xlsx");
            using var workbook = new XLWorkbook();
            build(workbook);
            workbook.SaveAs(path);
            return path;
        }

        [Fact]
        public async Task CustomerService_ShouldNormalizeAndReturnSavedCustomer()
        {
            using var factory = new TestDbContextFactory();
            var service = new CustomerService(factory, new LocalMasterDataReadRepository(factory));

            await service.SaveCustomerAsync(new Customer
            {
                CustomerNameEN = "  Alpha Trading  ",
                NotifyPartyName = "  Alpha Notify  ",
                TaxId = "  91310000ALPHA  "
            });

            var customer = await service.GetCustomerByNameAsync(" Alpha Trading ");

            Assert.NotNull(customer);
            Assert.Equal("Alpha Trading", customer.CustomerNameEN);
            Assert.Equal("Alpha Notify", customer.NotifyPartyName);
            Assert.Equal("91310000ALPHA", customer.TaxId);
        }

        [Fact]
        public async Task ProductService_ShouldNormalizeCodeHsCodeAndUnits()
        {
            using var factory = new TestDbContextFactory();
            var service = new ProductService(factory, new LocalMasterDataReadRepository(factory));

            await service.AddProductAsync(new Product
            {
                ProductCode = "  sku-001  ",
                NameEN = "  Cotton Shirt  ",
                HSCode = "  62052000  ",
                UnitEN = "  pcs  ",
                PackageUnitEN = "  ctn  "
            });

            var product = await service.GetByCodeAsync("sku-001");

            Assert.NotNull(product);
            Assert.Equal("sku-001", product.ProductCode);
            Assert.Equal("Cotton Shirt", product.NameEN);
            Assert.Equal("62052000", product.HSCode);
            Assert.Equal("PCS", product.UnitEN);
            Assert.Equal("CTN", product.PackageUnitEN);
        }

        [Fact]
        public async Task AuxiliaryService_ShouldNormalizePortsAndUnits()
        {
            using var factory = new TestDbContextFactory();
            var repository = new LocalMasterDataReadRepository(factory);
            var service = new AuxiliaryService(factory, repository, repository);

            await service.SavePortAsync(new Port
            {
                Code = "  cnnbo  ",
                NameEN = "  Ningbo  ",
                NameCN = "  宁波  ",
                Country = "  China  "
            });
            await service.SaveUnitAsync(new Unit
            {
                Code = "  pcs  ",
                NameEN = "  Piece  ",
                NameCN = "  件  "
            });

            var port = Assert.Single(await service.SearchPortsAsync("CNNBO"));
            var unit = Assert.Single(await service.SearchUnitsAsync("PCS"));

            Assert.Equal("CNNBO", port.Code);
            Assert.Equal("Ningbo", port.NameEN);
            Assert.Equal("宁波", port.NameCN);
            Assert.Equal("China", port.Country);
            Assert.Equal("PCS", unit.Code);
            Assert.Equal("Piece", unit.NameEN);
            Assert.Equal("件", unit.NameCN);
        }

        [Fact]
        public async Task MasterDataDeleteServices_ShouldRemoveExistingRowsWithRowVersion()
        {
            using var factory = new SqliteTestDbContextFactory(new AuditInterceptor());
            var repository = new LocalMasterDataReadRepository(factory);
            var customerService = new CustomerService(factory, repository);
            var exporterService = new ExporterService(factory, repository);
            var payeeService = new PayeeService(factory, repository);

            int customerId = await customerService.SaveCustomerAsync(new Customer
            {
                CustomerNameEN = "Delete Buyer"
            });
            int exporterId = await exporterService.SaveExporterAsync(new Exporter
            {
                ExporterNameEN = "Delete Exporter"
            });
            int payeeId = await payeeService.SavePayeeAsync(new Payee
            {
                Category = "Factory",
                Name = "Delete Payee"
            });

            Assert.True(await customerService.DeleteCustomerAsync(customerId));
            Assert.True(await exporterService.DeleteExporterAsync(exporterId));
            Assert.True(await payeeService.DeletePayeeAsync(payeeId));
            Assert.False(await customerService.DeleteCustomerAsync(customerId));
            Assert.False(await exporterService.DeleteExporterAsync(exporterId));
            Assert.False(await payeeService.DeletePayeeAsync(payeeId));

            await using var verifyContext = await factory.CreateDbContextAsync();
            Assert.Empty(await verifyContext.Customers.ToListAsync());
            Assert.Empty(await verifyContext.Exporters.ToListAsync());
            Assert.Empty(await verifyContext.Payees.ToListAsync());
        }

        private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly DbContextOptions<AppDbContext> _options;

            public TestDbContextFactory()
            {
                _options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options;
            }

            public AppDbContext CreateDbContext()
            {
                return new AppDbContext(_options);
            }

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CreateDbContext());
            }

            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
            }
        }

        private sealed class SqliteTestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
        {
            private readonly SqliteConnection _connection;
            private readonly DbContextOptions<AppDbContext> _options;

            public SqliteTestDbContextFactory(params IInterceptor[] interceptors)
            {
                _connection = new SqliteConnection("Data Source=:memory:");
                _connection.Open();

                var builder = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(_connection);

                if (interceptors != null && interceptors.Length > 0)
                {
                    builder.AddInterceptors(interceptors);
                }

                _options = builder.Options;

                using var context = CreateDbContext();
                context.Database.EnsureCreated();
            }

            public AppDbContext CreateDbContext()
            {
                return new AppDbContext(_options);
            }

            public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(CreateDbContext());
            }

            public void Dispose()
            {
                using var context = CreateDbContext();
                context.Database.EnsureDeleted();
                _connection.Dispose();
                SqliteConnection.ClearAllPools();
            }
        }
    }
}

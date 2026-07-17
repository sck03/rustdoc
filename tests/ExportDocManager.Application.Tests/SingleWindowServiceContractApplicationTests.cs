using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowServiceContractApplicationTests
    {
        [Fact]
        public void ServiceContracts_ShouldLiveInApplicationAssembly()
        {
            var applicationAssembly = typeof(SingleWindowBusinessType).Assembly;

            Assert.Equal(applicationAssembly, typeof(ICustomsCooDocumentService).Assembly);
            Assert.Equal(applicationAssembly, typeof(IAgentConsignmentDocumentService).Assembly);
            Assert.Equal(applicationAssembly, typeof(ISingleWindowDocumentPersistenceService).Assembly);
            Assert.Equal(applicationAssembly, typeof(ISingleWindowHandoffPackageService).Assembly);
            Assert.Equal(applicationAssembly, typeof(ISingleWindowExportReviewService).Assembly);
            Assert.Equal(applicationAssembly, typeof(ISingleWindowClientProfileService).Assembly);
            Assert.Equal(applicationAssembly, typeof(ISingleWindowClientBridge).Assembly);
            Assert.Equal(applicationAssembly, typeof(ICustomsCooFieldMapper).Assembly);
            Assert.Equal(applicationAssembly, typeof(IAgentConsignmentFieldMapper).Assembly);
        }

        [Fact]
        public void DocumentServiceContracts_ShouldKeepEntityAndDtoTypes()
        {
            Assert.Equal(
                typeof(Task<CustomsCooDocument>),
                typeof(ICustomsCooDocumentService)
                    .GetMethod(nameof(ICustomsCooDocumentService.GetOrCreateAsync))
                    ?.ReturnType);
            Assert.Equal(
                typeof(Task<AgentConsignmentDocument>),
                typeof(IAgentConsignmentDocumentService)
                    .GetMethod(nameof(IAgentConsignmentDocumentService.BuildDefaultsAsync))
                    ?.ReturnType);
            Assert.Equal(
                typeof(Task<int>),
                typeof(ISingleWindowDocumentPersistenceService)
                    .GetMethod(nameof(ISingleWindowDocumentPersistenceService.UpsertCustomsCooDocumentAsync))
                    ?.ReturnType);
            Assert.Equal(
                typeof(CooMappedDocument),
                typeof(ICustomsCooFieldMapper)
                    .GetMethod(nameof(ICustomsCooFieldMapper.Map))
                    ?.ReturnType);
            Assert.Equal(
                typeof(AcdMappedDocument),
                typeof(IAgentConsignmentFieldMapper)
                    .GetMethod(nameof(IAgentConsignmentFieldMapper.Map))
                    ?.ReturnType);
        }
    }
}

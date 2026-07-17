using System.Net;
using System.Net.Http.Json;
using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests
{
    public sealed class ApiContainerPackingProjectEndpointIntegrationTests
    {
        [Fact]
        public async Task ContainerPackingProjectEndpoints_ShouldPersistProjectsAndTypesInRuntimeDatabase()
        {
            await using var harness = await ApiIntegrationTestHarness.StartAsync(
                "edm-api-container-packing-projects",
                "api-container-packing-projects.db");
            using var anonymousClient = harness.CreateClient();

            var anonymousProjectsResponse = await anonymousClient.GetAsync("/api/tools/container-packing/projects");
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousProjectsResponse.StatusCode);

            var adminLogin = await harness.LoginAsync(anonymousClient, "admin", string.Empty);
            using var adminClient = harness.CreateClient(adminLogin.AccessToken);

            var containerTypesResponse = await adminClient.GetAsync("/api/tools/container-packing/container-types");
            Assert.Equal(HttpStatusCode.OK, containerTypesResponse.StatusCode);
            var containerTypes = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerTypeListResponse>(containerTypesResponse);
            Assert.Contains(containerTypes.ContainerTypes, type => type.Name == "20GP" && type.IsSystemDefault);
            Assert.Contains("ContainerTypeDefinitions", containerTypes.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("付款或报销", containerTypes.StoragePolicy, StringComparison.Ordinal);

            var defaultType = containerTypes.ContainerTypes.First(type => type.Name == "20GP");
            var defaultDeleteResponse = await adminClient.DeleteAsync($"/api/tools/container-packing/container-types/{defaultType.Id}");
            Assert.Equal(HttpStatusCode.Conflict, defaultDeleteResponse.StatusCode);

            var invalidProjectResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/container-packing/projects",
                new ApiContainerPackingProjectSaveRequest
                {
                    Name = "无货物方案",
                    Container = new ApiContainerDimensionsDto { Length = 589, Width = 235, Height = 239 }
                });
            Assert.Equal(HttpStatusCode.BadRequest, invalidProjectResponse.StatusCode);

            var customTypeResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/container-packing/container-types",
                new ApiContainerTypeSaveRequest
                {
                    Name = "API自定义柜",
                    Length = 610,
                    Width = 240,
                    Height = 245,
                    MaxVolume = 35.5m,
                    MaxWeight = 22000m
                });
            Assert.Equal(HttpStatusCode.OK, customTypeResponse.StatusCode);
            var customType = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerTypeSaveResponse>(customTypeResponse);
            Assert.True(customType.Id > 0);
            Assert.False(customType.ContainerType.IsSystemDefault);

            var createProjectResponse = await adminClient.PostAsJsonAsync(
                "/api/tools/container-packing/projects",
                CreateProjectRequest("API装柜方案", customType.ContainerType.Name, customType.ContainerType.Length, customType.ContainerType.Width, customType.ContainerType.Height));
            Assert.Equal(HttpStatusCode.OK, createProjectResponse.StatusCode);
            var createdProject = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerPackingProjectSaveResponse>(createProjectResponse);
            Assert.True(createdProject.Id > 0);
            Assert.Single(createdProject.Project.CargoItems);
            Assert.Equal("API装柜方案", createdProject.Project.Name);
            Assert.Contains("ContainerProjects", createdProject.StoragePolicy, StringComparison.Ordinal);
            Assert.Contains("不会读写发票", createdProject.StoragePolicy, StringComparison.Ordinal);

            var listProjectsResponse = await adminClient.GetAsync("/api/tools/container-packing/projects");
            Assert.Equal(HttpStatusCode.OK, listProjectsResponse.StatusCode);
            var projectList = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerPackingProjectListResponse>(listProjectsResponse);
            Assert.Contains(projectList.Projects, project => project.Id == createdProject.Id && project.ContainerType == customType.ContainerType.Name);

            var detailResponse = await adminClient.GetAsync($"/api/tools/container-packing/projects/{createdProject.Id}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerPackingProjectResponse>(detailResponse);
            Assert.Equal(createdProject.Id, detail.Project.Id);
            Assert.Equal("Door", detail.Project.CargoItems[0].PreferredZone);

            var updateRequest = CreateProjectRequest(
                "API装柜方案-更新",
                customType.ContainerType.Name,
                customType.ContainerType.Length,
                customType.ContainerType.Width,
                customType.ContainerType.Height);
            updateRequest.Id = createdProject.Id;
            updateRequest.CargoItems[0].Quantity = 3;

            var updateProjectResponse = await adminClient.PostAsJsonAsync("/api/tools/container-packing/projects", updateRequest);
            Assert.Equal(HttpStatusCode.OK, updateProjectResponse.StatusCode);
            var updatedProject = await ApiIntegrationTestHarness.ReadJsonAsync<ApiContainerPackingProjectSaveResponse>(updateProjectResponse);
            Assert.Equal(createdProject.Id, updatedProject.Id);
            Assert.Equal("API装柜方案-更新", updatedProject.Project.Name);
            Assert.Equal(3, updatedProject.Project.CargoItems[0].Quantity);

            var deleteProjectResponse = await adminClient.DeleteAsync($"/api/tools/container-packing/projects/{createdProject.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteProjectResponse.StatusCode);

            var deletedProjectDetailResponse = await adminClient.GetAsync($"/api/tools/container-packing/projects/{createdProject.Id}");
            Assert.Equal(HttpStatusCode.NotFound, deletedProjectDetailResponse.StatusCode);

            var deleteCustomTypeResponse = await adminClient.DeleteAsync($"/api/tools/container-packing/container-types/{customType.Id}");
            Assert.Equal(HttpStatusCode.OK, deleteCustomTypeResponse.StatusCode);
        }

        private static ApiContainerPackingProjectSaveRequest CreateProjectRequest(
            string name,
            string containerType,
            int length,
            int width,
            int height)
        {
            return new ApiContainerPackingProjectSaveRequest
            {
                Name = name,
                ContainerType = containerType,
                Container = new ApiContainerDimensionsDto
                {
                    Length = length,
                    Width = width,
                    Height = height,
                    Volume = 35.5m,
                    MaxWeight = 22000m
                },
                Rules = new ApiContainerPackingRulesDto
                {
                    AllowRotation = true,
                    UsePalletConstraints = true,
                    DefaultPalletLength = 120,
                    DefaultPalletWidth = 100,
                    DefaultPalletHeight = 15,
                    DefaultPalletWeight = 25m,
                    EnforceCenterOfGravity = true,
                    CenterOfGravityTolerancePercent = 20m,
                    MinimumSupportAreaPercent = 80m,
                    RequireSameFootprintStacking = true
                },
                CargoItems =
                [
                    new ApiContainerPackingCargoInputDto
                    {
                        Name = "API货物",
                        Length = 60m,
                        Width = 40m,
                        Height = 40m,
                        Weight = 10m,
                        Quantity = 2,
                        ColorArgb = unchecked((int)0xFF4287F5),
                        UsePallet = true,
                        UnitsPerPallet = 10,
                        MaxTopLoadWeight = 120m,
                        PreferredZone = "Door",
                        LoadSequence = 1,
                        PriorityGroup = "A"
                    }
                ]
            };
        }
    }
}

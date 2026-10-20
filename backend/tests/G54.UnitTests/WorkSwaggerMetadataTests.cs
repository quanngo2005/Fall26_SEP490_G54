using System.ComponentModel.DataAnnotations;
using System.Reflection;
using G54.Api.Controllers;
using G54.Api.Swagger.Examples;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Work;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Filters;

namespace G54.UnitTests;

public sealed class WorkSwaggerMetadataTests
{
    [Fact]
    public void WorksController_ClassLevel_DeclaresUnauthorizedAndInternalServerErrorResponses()
    {
        var classResponses = typeof(WorksController)
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToList();

        Assert.Contains(classResponses, r => r.StatusCode == StatusCodes.Status401Unauthorized && r.Type == typeof(ApiResponse));
        Assert.Contains(classResponses, r => r.StatusCode == StatusCodes.Status500InternalServerError && r.Type == typeof(ApiResponse));
    }

    [Fact]
    public void CreateAction_HasExpectedProducesResponseTypeAttributes()
    {
        var method = typeof(WorksController).GetMethod(nameof(WorksController.Create));
        Assert.NotNull(method);

        var methodResponses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        var allResponses = methodResponses
            .Concat(typeof(WorksController).GetCustomAttributes<ProducesResponseTypeAttribute>())
            .ToList();

        Assert.Contains(methodResponses, r => r.StatusCode == StatusCodes.Status201Created && r.Type == typeof(ApiResponse<WorkDetailDto>));
        Assert.Contains(methodResponses, r => r.StatusCode == StatusCodes.Status400BadRequest && r.Type == typeof(ApiResponse));
        Assert.Contains(methodResponses, r => r.StatusCode == StatusCodes.Status404NotFound && r.Type == typeof(ApiResponse));

        Assert.Contains(allResponses, r => r.StatusCode == StatusCodes.Status401Unauthorized && r.Type == typeof(ApiResponse));
        Assert.Contains(allResponses, r => r.StatusCode == StatusCodes.Status500InternalServerError && r.Type == typeof(ApiResponse));
    }

    [Fact]
    public void GetByIdAction_HasExpectedProducesResponseTypeAttributes()
    {
        var method = typeof(WorksController).GetMethod(nameof(WorksController.GetById));
        Assert.NotNull(method);

        var methodResponses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToList();
        var allResponses = methodResponses
            .Concat(typeof(WorksController).GetCustomAttributes<ProducesResponseTypeAttribute>())
            .ToList();

        Assert.Contains(methodResponses, r => r.StatusCode == StatusCodes.Status200OK && r.Type == typeof(ApiResponse<WorkDetailDto>));
        Assert.Contains(methodResponses, r => r.StatusCode == StatusCodes.Status404NotFound && r.Type == typeof(ApiResponse));

        Assert.Contains(allResponses, r => r.StatusCode == StatusCodes.Status401Unauthorized && r.Type == typeof(ApiResponse));
        Assert.Contains(allResponses, r => r.StatusCode == StatusCodes.Status500InternalServerError && r.Type == typeof(ApiResponse));
    }

    [Fact]
    public void CreateAction_HasSwaggerExampleAttributesAttached()
    {
        var method = typeof(WorksController).GetMethod(nameof(WorksController.Create));
        Assert.NotNull(method);

        var requestExample = method.GetCustomAttribute<SwaggerRequestExampleAttribute>();
        Assert.NotNull(requestExample);
        Assert.Equal(typeof(CreateWorkRequestDto), requestExample.RequestType);
        Assert.Equal(typeof(CreateWorkRequestExample), requestExample.ExamplesProviderType);

        var responseExamples = method.GetCustomAttributes<SwaggerResponseExampleAttribute>().ToList();
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status201Created && e.ExamplesProviderType == typeof(CreateWorkSuccessExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status400BadRequest && e.ExamplesProviderType == typeof(WorkCreateBadRequestExamples));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status401Unauthorized && e.ExamplesProviderType == typeof(WorkUnauthorizedExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status404NotFound && e.ExamplesProviderType == typeof(WorkCreateNotFoundExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status500InternalServerError && e.ExamplesProviderType == typeof(WorkInternalErrorExample));
    }

    [Fact]
    public void GetByIdAction_HasSwaggerResponseExampleAttributesAttached()
    {
        var method = typeof(WorksController).GetMethod(nameof(WorksController.GetById));
        Assert.NotNull(method);

        var responseExamples = method.GetCustomAttributes<SwaggerResponseExampleAttribute>().ToList();
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status200OK && e.ExamplesProviderType == typeof(GetWorkSuccessExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status401Unauthorized && e.ExamplesProviderType == typeof(WorkUnauthorizedExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status404NotFound && e.ExamplesProviderType == typeof(WorkGetNotFoundExample));
        Assert.Contains(responseExamples, e => e.StatusCode == StatusCodes.Status500InternalServerError && e.ExamplesProviderType == typeof(WorkInternalErrorExample));
    }

    [Fact]
    public void CreateWorkRequestExample_PassesDataAnnotationsValidation()
    {
        var provider = new CreateWorkRequestExample();
        var request = provider.GetExamples();

        Assert.NotNull(request);
        Assert.False(string.IsNullOrWhiteSpace(request.Title));
        Assert.InRange(request.Title.Length, 5, 255);
        Assert.NotEqual(Guid.Empty, request.WorkTypeId);
        Assert.NotEqual(Guid.Empty, request.AssigneeEmployeeId);

        var context = new ValidationContext(request);
        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(request, context, validationResults, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(validationResults);
    }

    [Fact]
    public void SuccessExampleProviders_ReturnValidObjects()
    {
        var createSuccessProvider = new CreateWorkSuccessExample();
        var createResponse = createSuccessProvider.GetExamples();

        Assert.NotNull(createResponse);
        Assert.True(createResponse.Success);
        Assert.Equal(200, createResponse.Status);
        Assert.Equal("Khởi tạo và phân công công việc thành công.", createResponse.Message);
        Assert.NotNull(createResponse.Data);
        Assert.Equal("High", createResponse.Data.Priority);
        Assert.Equal("New", createResponse.Data.Status);

        var getSuccessProvider = new GetWorkSuccessExample();
        var getResponse = getSuccessProvider.GetExamples();

        Assert.NotNull(getResponse);
        Assert.True(getResponse.Success);
        Assert.Equal(200, getResponse.Status);
        Assert.NotNull(getResponse.Data);
        Assert.Equal("High", getResponse.Data.Priority);
        Assert.Equal("New", getResponse.Data.Status);
    }

    [Theory]
    [InlineData(typeof(WorkValidationErrorExample), StatusCodes.Status400BadRequest, ErrorCodes.ValidationError)]
    [InlineData(typeof(WorkCampaignRuleErrorExample), StatusCodes.Status400BadRequest, ErrorCodes.MSG18)]
    [InlineData(typeof(WorkUnauthorizedExample), StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized)]
    [InlineData(typeof(WorkCreateNotFoundExample), StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData(typeof(WorkGetNotFoundExample), StatusCodes.Status404NotFound, ErrorCodes.NotFound)]
    [InlineData(typeof(WorkInternalErrorExample), StatusCodes.Status500InternalServerError, ErrorCodes.InternalError)]
    public void ErrorExampleProviders_ReturnConsistentErrorEnvelope(Type providerType, int expectedStatus, string expectedErrorCode)
    {
        var provider = Activator.CreateInstance(providerType);
        Assert.NotNull(provider);

        var method = providerType.GetMethod(nameof(IExamplesProvider<ApiResponse>.GetExamples));
        Assert.NotNull(method);

        var response = method.Invoke(provider, null) as ApiResponse;
        Assert.NotNull(response);

        Assert.False(response.Success);
        Assert.Equal(expectedStatus, response.Status);
        Assert.Equal(expectedErrorCode, response.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
        Assert.False(string.IsNullOrWhiteSpace(response.TraceId));
    }

    [Fact]
    public void XmlDocumentation_FilesExistAndContainRequiredSymbols()
    {
        var apiXmlPath = Path.Combine(AppContext.BaseDirectory, "G54.Api.xml");
        var bllXmlPath = Path.Combine(AppContext.BaseDirectory, "G54.BLL.xml");

        Assert.True(File.Exists(apiXmlPath), $"Missing XML documentation file: {apiXmlPath}");
        Assert.True(File.Exists(bllXmlPath), $"Missing XML documentation file: {bllXmlPath}");

        var apiXmlContent = File.ReadAllText(apiXmlPath);
        var bllXmlContent = File.ReadAllText(bllXmlPath);

        Assert.Contains("M:G54.Api.Controllers.WorksController.Create", apiXmlContent, StringComparison.Ordinal);
        Assert.Contains("M:G54.Api.Controllers.WorksController.GetById", apiXmlContent, StringComparison.Ordinal);
        Assert.Contains("T:G54.BLL.Dtos.Work.CreateWorkRequestDto", bllXmlContent, StringComparison.Ordinal);
        Assert.Contains("T:G54.BLL.Dtos.Work.WorkDetailDto", bllXmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkCreateBadRequestExamples_ProvidesValidationAndCampaignRuleExamples()
    {
        var examples = new WorkCreateBadRequestExamples().GetExamples().ToList();

        Assert.Equal(2, examples.Count);
        Assert.Contains(examples, e => e.Name == "VALIDATION_ERROR" && e.Value.ErrorCode == ErrorCodes.ValidationError);
        Assert.Contains(examples, e => e.Name == "MSG18" && e.Value.ErrorCode == ErrorCodes.MSG18);
        Assert.All(examples, e => Assert.Equal(StatusCodes.Status400BadRequest, e.Value.Status));
    }
}

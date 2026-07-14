using System.Text.Json;
using FairShareMonApi.Constants;
using FairShareMonApi.Exceptions;
using FairShareMonApi.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the <see cref="ApiResult"/> envelope: factory shapes, HTTP status
/// derivation, and the serialized JSON contract <c>{ data, isSuccess, error{code,message} }</c>
/// (+ optional <c>error.fields</c>). Assertions target stable error CODES, never message text.
/// </summary>
public class ApiResultTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static JsonElement SerializeToElement(ApiResult result) =>
        JsonSerializer.SerializeToElement<object>(result, WebJson);

    [Fact]
    public void Success_WithData_SetsDataAndNoError()
    {
        var result = ApiResult<string>.Success("payload");

        Assert.True(result.IsSuccess);
        Assert.Equal("payload", result.Data);
        Assert.Null(result.Error);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public void Success_WithCustomStatusCode_UsesIt()
    {
        var result = ApiResult<string>.Success("payload", StatusCodes.Status201Created);

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    [Fact]
    public void SuccessMessage_Message_WrapsMessageIntoDataObject()
    {
        var result = ApiResult.SuccessMessage("thao tác thành công");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);

        var root = SerializeToElement(result);
        Assert.Equal("thao tác thành công", root.GetProperty("data").GetProperty("message").GetString());
    }

    [Fact]
    public void Success_Serialized_ProducesEnvelopeShapeWithNullErrorAndNoStatusCode()
    {
        var root = SerializeToElement(ApiResult<int>.Success(42));

        Assert.Equal(42, root.GetProperty("data").GetInt32());
        Assert.True(root.GetProperty("isSuccess").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.False(root.TryGetProperty("statusCode", out _)); // [JsonIgnore] - transport-only
    }

    [Theory]
    [InlineData(ErrorCodes.InternalError, StatusCodes.Status500InternalServerError)]
    [InlineData(ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(9999, StatusCodes.Status500InternalServerError)] // unknown codes fall back to 500
    public void Failure_ByCode_DerivesDefaultHttpStatus(int code, int expectedStatus)
    {
        var result = ApiResult.Failure(code, "thông báo lỗi");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error!.Code);
        Assert.Equal(expectedStatus, result.StatusCode);
    }

    [Fact]
    public void Failure_WithExplicitStatusCode_OverridesDefaultMapping()
    {
        var result = ApiResult.Failure(ErrorCodes.NotFound, "thông báo lỗi", statusCode: StatusCodes.Status410Gone);

        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
    }

    [Fact]
    public void Failure_Serialized_ProducesErrorEnvelopeWithNullData()
    {
        var root = SerializeToElement(ApiResult.Failure(ErrorCodes.NotFound, "không tìm thấy"));

        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.False(root.GetProperty("isSuccess").GetBoolean());
        var error = root.GetProperty("error");
        Assert.Equal(ErrorCodes.NotFound, error.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public void Failure_WithFields_ExposesFieldErrorsUnderErrorFields()
    {
        // Contract: producers (ErrorHandlerFilter) supply camelCase field keys to match the envelope.
        var fields = new Dictionary<string, string[]> { ["name"] = ["bắt buộc", "quá dài"] };

        var result = ApiResult.Failure(ErrorCodes.ValidationFailed, "dữ liệu không hợp lệ", fields);

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        var fieldErrors = SerializeToElement(result).GetProperty("error").GetProperty("fields").GetProperty("name");
        Assert.Equal(2, fieldErrors.GetArrayLength());
    }

    [Fact]
    public void Failure_WithoutFields_OmitsFieldsFromJson()
    {
        var error = SerializeToElement(ApiResult.Failure(ErrorCodes.ValidationFailed, "dữ liệu không hợp lệ")).GetProperty("error");

        Assert.False(error.TryGetProperty("fields", out _));
    }

    [Fact]
    public void Failure_FromErrorException_CopiesCodeMessageAndStatus()
    {
        var exception = new ErrorException(ErrorCodes.Unauthorized, "phiên không hợp lệ");

        var result = ApiResult.Failure(exception);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCodes.Unauthorized, result.Error!.Code);
        Assert.Equal(exception.Message, result.Error.Message);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);
    }
}

/// <summary>Pure unit tests for <see cref="ErrorException"/>'s code -> HTTP status mapping.</summary>
public class ErrorExceptionTests
{
    [Theory]
    [InlineData(ErrorCodes.InternalError, StatusCodes.Status500InternalServerError)]
    [InlineData(ErrorCodes.ValidationFailed, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorCodes.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorCodes.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(4242, StatusCodes.Status500InternalServerError)] // unknown codes fall back to 500
    public void GetDefaultHttpStatus_KnownCodes_MapToExpectedStatus(int code, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ErrorException.GetDefaultHttpStatus(code));
    }

    [Fact]
    public void Constructor_WithoutExplicitStatus_UsesDefaultMapping()
    {
        var exception = new ErrorException(ErrorCodes.NotFound, "không tìm thấy");

        Assert.Equal(ErrorCodes.NotFound, exception.Code);
        Assert.Equal(StatusCodes.Status404NotFound, exception.HttpStatus);
    }

    [Fact]
    public void Constructor_WithExplicitStatus_KeepsIt()
    {
        var exception = new ErrorException(ErrorCodes.NotFound, "đã bị xoá", StatusCodes.Status410Gone);

        Assert.Equal(StatusCodes.Status410Gone, exception.HttpStatus);
    }
}

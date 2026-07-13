using FairShareMonApi.Attributes;
using FairShareMonApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace FairShareMonApi.Tests;

/// <summary>
/// Pure unit tests for the <see cref="ResponseWrappedAttribute"/> result filter: plain 2xx object
/// results are wrapped into the success envelope, while non-2xx object results and results that
/// are already an <see cref="ApiResult"/> pass through untouched (a non-2xx payload must never be
/// mislabeled <c>isSuccess: true</c>).
/// </summary>
public class ResponseWrappedAttributeTests
{
    private static ResultExecutingContext CreateContext(IActionResult result) =>
        new(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            [],
            result,
            controller: new object());

    [Fact]
    public void OnResultExecuting_PlainDtoWithoutStatusCode_WrapsInto200SuccessEnvelope()
    {
        var context = CreateContext(new ObjectResult(new { Value = 42 }));

        new ResponseWrappedAttribute().OnResultExecuting(context);

        var wrapped = Assert.IsAssignableFrom<ApiResult>(context.Result);
        Assert.True(wrapped.IsSuccess);
        Assert.Null(wrapped.Error);
        Assert.Equal(StatusCodes.Status200OK, wrapped.StatusCode);
    }

    [Fact]
    public void OnResultExecuting_PlainDtoWith201_WrapsAndKeepsStatusCode()
    {
        var context = CreateContext(new ObjectResult(new { Value = 42 }) { StatusCode = StatusCodes.Status201Created });

        new ResponseWrappedAttribute().OnResultExecuting(context);

        var wrapped = Assert.IsAssignableFrom<ApiResult>(context.Result);
        Assert.True(wrapped.IsSuccess);
        Assert.Equal(StatusCodes.Status201Created, wrapped.StatusCode);
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status302Found)]
    public void OnResultExecuting_NonSuccessObjectResult_IsLeftUntouched(int statusCode)
    {
        var original = new ObjectResult(new { Reason = "lỗi" }) { StatusCode = statusCode };
        var context = CreateContext(original);

        new ResponseWrappedAttribute().OnResultExecuting(context);

        Assert.Same(original, context.Result);
    }

    [Fact]
    public void OnResultExecuting_ValueAlreadyApiResult_IsLeftUntouched()
    {
        var original = new ObjectResult(ApiResult.SuccessMessage("đã bọc sẵn"));
        var context = CreateContext(original);

        new ResponseWrappedAttribute().OnResultExecuting(context);

        Assert.Same(original, context.Result);
    }
}

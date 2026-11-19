using System.Net;
using System.Text.Json;
using Shouldly;

namespace AppTemplate.Api.Tests.Infrastructure;

public static class ApiResponseShouldly
{
    public static void ShouldFailWith(this ApiResponse r, string code, HttpStatusCode? status = null)
    {
        if (status is not null)
        {
            r.Status.ShouldBe(status.Value, r.Body.ToString());
        }

        r.ContentType.ShouldBe("application/problem+json", r.Body.ToString());
        r.Body.ValueKind.ShouldBe(JsonValueKind.Object);
        r.Body.GetProperty("code").GetString().ShouldBe(code, r.Body.ToString());
        r.Body.GetProperty("status").GetInt32().ShouldBe((int)r.Status);
        r.Body.TryGetProperty("details", out _).ShouldBeTrue("missing extension 'details'");
        r.Body.GetProperty("suggestion").GetString().ShouldNotBeNullOrWhiteSpace();
        var traceId = r.Body.GetProperty("traceId").GetString();
        traceId.ShouldNotBeNullOrWhiteSpace();
        traceId.ShouldBe(r.TraceId, "body traceId must match the X-Trace-Id header");
    }

    public static void ShouldHaveFieldError(this ApiResponse r, string field, string code)
    {
        r.ShouldFailWith("validation.failed", HttpStatusCode.UnprocessableEntity);
        var fields = r.Body.GetProperty("details").GetProperty("fields").EnumerateArray().ToList();
        fields.ShouldContain(
            f => f.GetProperty("field").GetString() == field && f.GetProperty("code").GetString() == code,
            $"no field error {field}/{code} in {r.Body}");
    }

    public static void ShouldSucceed(this ApiResponse r)
    {
        ((int)r.Status).ShouldBeInRange(200, 299, r.Body.ToString());
    }
}

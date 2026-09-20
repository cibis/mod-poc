using Microsoft.AspNetCore.Mvc;

namespace Mod.Platform.Common.Errors;

public static class ProblemDetailsFactory
{
    public static ProblemDetails Create(int status, string errorCode, string? detail = null) =>
        new()
        {
            Type = errorCode,
            Status = status,
            Detail = detail
        };
}

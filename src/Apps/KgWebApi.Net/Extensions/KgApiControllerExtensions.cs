using KuGou.Net.Abstractions.Models;
using KgWebApi.Net.Models;
using Microsoft.AspNetCore.Mvc;

namespace KgWebApi.Net.Extensions;

public static class KgApiControllerExtensions
{
    public static IActionResult FromKgStatus<T>(this ControllerBase controller, T? result) where T : KgBaseModel
    {
        if (result?.Status == null || result.Status == 1)
            return controller.Ok(result);

        return controller.BadRequest(result);
    }

    public static IActionResult ApiBadRequest(this ControllerBase controller, string msg, int errorCode = 40000)
    {
        return controller.BadRequest(new ApiErrorResponse(0, msg, errorCode));
    }

    public static IActionResult ApiNotFound(this ControllerBase controller, string msg, int errorCode = 40400)
    {
        return controller.NotFound(new ApiErrorResponse(0, msg, errorCode));
    }

    public static IActionResult ApiServerError(this ControllerBase controller, string msg, int errorCode = 50000)
    {
        return controller.StatusCode(StatusCodes.Status500InternalServerError, new ApiErrorResponse(0, msg, errorCode));
    }

    public static IActionResult ApiGatewayTimeout(this ControllerBase controller, string msg, int errorCode = 50400)
    {
        return controller.StatusCode(StatusCodes.Status504GatewayTimeout, new ApiErrorResponse(0, msg, errorCode));
    }
}

using Microsoft.AspNetCore.Antiforgery;

namespace SquashClub.Web.Api;

public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(context.HttpContext.Request.Method) &&
            !HttpMethods.IsHead(context.HttpContext.Request.Method))
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        return await next(context);
    }
}

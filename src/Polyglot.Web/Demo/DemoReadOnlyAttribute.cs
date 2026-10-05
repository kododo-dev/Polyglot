using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Kododo.Polyglot.Web.Demo;

/// <summary>
/// In the demo, refuses the page's form posts: what they change, such as passwords and users, would
/// spoil the demo for the next visitor. The pages hide those forms in the demo, so only a request
/// made by hand gets here.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DemoReadOnlyAttribute : Attribute, IAsyncPageFilter
{
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var demo = context.HttpContext.RequestServices.GetRequiredService<DemoOptions>();
        if (demo.Enabled && HttpMethods.IsPost(context.HttpContext.Request.Method))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        await next();
    }
}

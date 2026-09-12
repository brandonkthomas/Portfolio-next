using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Portfolio.Web.Pages;

/// <summary>
/// Error page -- backing properties / helpers
/// </summary>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ErrorModel : PageModel
{
    public string Title { get; private set; } = "Request error";

    public string Message { get; private set; } = "The request could not be completed.";

    public void OnGet(int? statusCode)
    {
        var responseStatusCode = statusCode is >= 400 and <= 599 ? statusCode.Value : 500;
        Response.StatusCode = responseStatusCode;

        (Title, Message) = responseStatusCode switch
        {
            404 => ("Page not found", "The requested page does not exist."),
            500 => ("Server error", "The server could not complete the request."),
            _ => ("Request error", "The request could not be completed.")
        };
    }
}

using Microsoft.AspNetCore.Http;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DotLearn.Payment.Middleware;

public class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext != null &&
            _httpContextAccessor.HttpContext.Items.TryGetValue("CorrelationId", out var correlationId))
        {
            if (!request.Headers.Contains("X-Correlation-ID") && correlationId != null)
            {
                request.Headers.Add("X-Correlation-ID", correlationId.ToString());
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
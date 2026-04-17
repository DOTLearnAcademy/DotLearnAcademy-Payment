using DotLearn.Payment.Models.DTOs;
using DotLearn.Payment.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace DotLearn.Payment.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    // POST /api/payments/checkout
    [HttpPost("checkout")]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequestDto request)
    {
        try
        {
            var result = await _paymentService
                .CreateCheckoutAsync(request, GetUserId());
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /api/payments/verify
    [HttpPost("verify")]
    [Authorize(Roles = "Student,Admin")]
    public async Task<IActionResult> Verify(
        [FromBody] VerifyPaymentRequestDto request)
    {
        try
        {
            var result = await _paymentService
                .VerifyPaymentAsync(request, GetUserId());
            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /api/payments/webhook/razorpay
    [HttpPost("webhook/razorpay")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        // Read raw body for signature verification
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync();
        var signature = Request.Headers["X-Razorpay-Signature"]
            .FirstOrDefault() ?? string.Empty;

        try
        {
            await _paymentService.HandleWebhookAsync(payload, signature);
            return Ok(new { message = "Webhook processed." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /api/payments/refund
    [HttpPost("refund")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Refund(
        [FromBody] RefundRequestDto request)
    {
        try
        {
            var result = await _paymentService
                .RefundAsync(request, GetUserId());
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // GET /api/payments/student/{id}
    [HttpGet("student/{id}")]
    [Authorize]
    public async Task<IActionResult> GetByStudent(Guid id)
    {
        var requesterId = GetUserId();
        var requesterRole = GetUserRole();

        // Students can only view their own payments
        if (requesterRole != "Admin" && requesterId != id)
            return Forbid();

        var result = await _paymentService.GetByStudentIdAsync(id);
        return Ok(result);
    }

    // ── Helpers ──────────────────────────────────────────────────
    private Guid GetUserId()
    {
        var userId =
            User.FindFirstValue(JwtRegisteredClaimNames.Sub) ??
            User.FindFirstValue(ClaimTypes.NameIdentifier) ??
            User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            throw new UnauthorizedAccessException("User ID not found in token.");

        return Guid.Parse(userId);
    }

    private string GetUserRole()
    {
        var claim = User.FindFirst(ClaimTypes.Role);
        if (claim == null)
            throw new UnauthorizedAccessException("Role not found in token.");
            
        return claim.Value;
    }
}

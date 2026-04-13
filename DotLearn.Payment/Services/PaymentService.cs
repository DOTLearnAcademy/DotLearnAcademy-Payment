using DotLearn.Payment.Models.DTOs;
using DotLearn.Payment.Models.Entities;
using DotLearn.Payment.Repositories;
using Razorpay.Api;
using System.Text.Json;

namespace DotLearn.Payment.Services;

public interface IRazorpayOrderService
{
    string CreateOrder(int amount, string currency, string receipt);
}

public class RazorpayOrderService : IRazorpayOrderService
{
    private readonly string _keyId;
    private readonly string _keySecret;

    public RazorpayOrderService(IConfiguration config)
    {
        _keyId = config["Razorpay:KeyId"] ?? "";
        _keySecret = config["Razorpay:KeySecret"] ?? "";
    }

    public string CreateOrder(int amount, string currency, string receipt)
    {
        var client = new RazorpayClient(_keyId, _keySecret);
        var options = new Dictionary<string, object>
        {
            { "amount", amount },
            { "currency", currency },
            { "receipt", receipt },
            { "payment_capture", 1 }
        };
        var order = client.Order.Create(options);
        return order["id"].ToString()!;
    }
}

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _repo;
    private readonly SqsService _sqsService;
    private readonly RazorpaySignatureService _signatureService;
    private readonly IConfiguration _config;
    private readonly IRazorpayOrderService _razorpayOrderService;

    public PaymentService(
        IPaymentRepository repo,
        SqsService sqsService,
        RazorpaySignatureService signatureService,
        IConfiguration config,
        IRazorpayOrderService razorpayOrderService = null!)
    {
        _repo = repo;
        _sqsService = sqsService;
        _signatureService = signatureService;
        _config = config;
        _razorpayOrderService = razorpayOrderService ?? new RazorpayOrderService(config);
    }

    public async Task<CheckoutResponseDto> CreateCheckoutAsync(
        CheckoutRequestDto request, Guid studentId)
    {
        var keyId = _config["Razorpay:KeyId"]!;
        var keySecret = _config["Razorpay:KeySecret"]!;

        // TODO: Call Course Service internal API to get real price
        // For now using placeholder amount
        var amount = 49900; // in paise (499.00 INR)

        string orderId = _razorpayOrderService.CreateOrder(amount, "INR", $"rcpt_{Guid.NewGuid():N}"[..21]);

        var payment = new Models.Entities.Payment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            CourseId = request.CourseId,
            Amount = amount / 100m,
            Currency = "INR",
            Provider = "razorpay",
            TransactionId = $"pending_{Guid.NewGuid():N}", // unique placeholder; replaced in VerifyPaymentAsync
            OrderId = orderId,
            Status = PaymentStatus.Pending,
            CompletedAt = null
        };
        await _repo.AddAsync(payment);

        return new CheckoutResponseDto(
            orderId,
            amount / 100m,
            "INR",
            keyId
        );
    }

    public async Task<PaymentResponseDto> VerifyPaymentAsync(
        VerifyPaymentRequestDto request, Guid studentId)
    {
        // Verify HMAC-SHA256 signature
        if (!_signatureService.VerifyPaymentSignature(
            request.OrderId, request.PaymentId, request.Signature))
            throw new UnauthorizedAccessException("Invalid payment signature.");

        // Idempotency check
        var existing = await _repo.GetByTransactionIdAsync(request.PaymentId);
        if (existing != null)
            return MapToDto(existing);

        // Get order to find course/amount
        var pendingPayment = await _repo.GetByOrderIdAsync(request.OrderId);
        
        if (pendingPayment == null) 
            throw new KeyNotFoundException("Pending payment not found for order.");

        pendingPayment.TransactionId = request.PaymentId;
        pendingPayment.Status = PaymentStatus.Succeeded;
        pendingPayment.CompletedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(pendingPayment);
        await _sqsService.PublishPaymentSucceededAsync(pendingPayment);

        return MapToDto(pendingPayment);
    }

    public async Task HandleWebhookAsync(string payload, string signature)
    {
        if (!_signatureService.VerifyWebhookSignature(payload, signature))
            throw new UnauthorizedAccessException("Invalid webhook signature.");

        var webhookData = JsonSerializer.Deserialize<JsonElement>(payload);
        var eventType = webhookData
            .GetProperty("event").GetString();

        if (eventType == "payment.captured")
        {
            var paymentEntity = webhookData
                .GetProperty("payload")
                .GetProperty("payment")
                .GetProperty("entity");

            var transactionId = paymentEntity
                .GetProperty("id").GetString()!;
            var orderId = paymentEntity
                .GetProperty("order_id").GetString()!;

            // Idempotency check
            var existing = await _repo.GetByTransactionIdAsync(transactionId);
            if (existing != null) return;

            var pendingPayment = await _repo.GetByOrderIdAsync(orderId);
            if (pendingPayment != null)
            {
                pendingPayment.TransactionId = transactionId;
                pendingPayment.Status = PaymentStatus.Succeeded;
                pendingPayment.CompletedAt = DateTime.UtcNow;
                await _repo.UpdateAsync(pendingPayment);
                await _sqsService.PublishPaymentSucceededAsync(pendingPayment);
            }
        }
        else if (eventType == "payment.failed")
        {
            var paymentEntity = webhookData
                .GetProperty("payload")
                .GetProperty("payment")
                .GetProperty("entity");

            var payment = new Models.Entities.Payment
            {
                Id = Guid.NewGuid(),
                StudentId = Guid.Empty,
                CourseId = Guid.Empty,
                Amount = 0,
                Currency = "INR",
                Provider = "razorpay",
                TransactionId = paymentEntity
                    .GetProperty("id").GetString()!,
                OrderId = paymentEntity
                    .GetProperty("order_id").GetString()!,
                Status = PaymentStatus.Failed
            };

            await _repo.AddAsync(payment);
            await _sqsService.PublishPaymentFailedAsync(payment);
        }
    }

    public async Task<PaymentResponseDto> RefundAsync(
        RefundRequestDto request, Guid requesterId)
    {
        var payment = await _repo.GetByIdAsync(request.PaymentId)
            ?? throw new KeyNotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Succeeded)
            throw new InvalidOperationException(
                "Only succeeded payments can be refunded.");

        payment.Status = PaymentStatus.Refunded;
        await _repo.UpdateAsync(payment);
        await _sqsService.PublishCourseAccessRevokedAsync(payment);

        return MapToDto(payment);
    }

    public async Task<List<PaymentResponseDto>> GetByStudentIdAsync(Guid studentId)
    {
        var payments = await _repo.GetByStudentIdAsync(studentId);
        return payments.Select(MapToDto).ToList();
    }

    private static PaymentResponseDto MapToDto(Models.Entities.Payment p) => new(
        p.Id, p.StudentId, p.CourseId, p.Amount, p.Currency,
        p.Provider, p.TransactionId, p.OrderId, p.Status.ToString(),
        p.CreatedAt, p.CompletedAt
    );
}

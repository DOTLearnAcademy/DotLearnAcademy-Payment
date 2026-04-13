using DotLearn.Payment.Models.DTOs;
using DotLearn.Payment.Models.Entities;
using DotLearn.Payment.Repositories;
using Razorpay.Api;
using System.Text.Json;

namespace DotLearn.Payment.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _repo;
    private readonly SqsService _sqsService;
    private readonly RazorpaySignatureService _signatureService;
    private readonly IConfiguration _config;

    public PaymentService(
        IPaymentRepository repo,
        SqsService sqsService,
        RazorpaySignatureService signatureService,
        IConfiguration config)
    {
        _repo = repo;
        _sqsService = sqsService;
        _signatureService = signatureService;
        _config = config;
    }

    public async Task<CheckoutResponseDto> CreateCheckoutAsync(
        CheckoutRequestDto request, Guid studentId)
    {
        var keyId = _config["Razorpay:KeyId"]!;
        var keySecret = _config["Razorpay:KeySecret"]!;

        // TODO: Call Course Service internal API to get real price
        // For now using placeholder amount
        var amount = 49900; // in paise (499.00 INR)

        var client = new RazorpayClient(keyId, keySecret);
        var options = new Dictionary<string, object>
        {
            { "amount", amount },
            { "currency", "INR" },
            { "receipt", $"rcpt_{Guid.NewGuid():N}"[..21] },
            { "payment_capture", 1 }
        };

        var order = client.Order.Create(options);
        var orderId = order["id"].ToString()!;

        var payment = new Models.Entities.Payment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            CourseId = request.CourseId,
            Amount = amount / 100m,
            Currency = "INR",
            Provider = "razorpay",
            TransactionId = string.Empty, // filled in after Razorpay confirms payment
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

        var payment = new Models.Entities.Payment
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            CourseId = pendingPayment?.CourseId ?? Guid.Empty,
            Amount = pendingPayment?.Amount ?? 0,
            Currency = "INR",
            Provider = "razorpay",
            TransactionId = request.PaymentId,
            OrderId = request.OrderId,
            Status = PaymentStatus.Succeeded,
            CompletedAt = DateTime.UtcNow
        };

        await _repo.AddAsync(payment);
        await _sqsService.PublishPaymentSucceededAsync(payment);

        return MapToDto(payment);
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

            var payment = new Models.Entities.Payment
            {
                Id = Guid.NewGuid(),
                StudentId = Guid.Empty, // resolved via orderId in real impl
                CourseId = Guid.Empty,
                Amount = paymentEntity.GetProperty("amount").GetDecimal() / 100,
                Currency = "INR",
                Provider = "razorpay",
                TransactionId = transactionId,
                OrderId = orderId,
                Status = PaymentStatus.Succeeded,
                CompletedAt = DateTime.UtcNow
            };

            await _repo.AddAsync(payment);
            await _sqsService.PublishPaymentSucceededAsync(payment);
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

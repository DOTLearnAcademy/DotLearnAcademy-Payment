namespace DotLearn.Payment.Models.DTOs;

public record CheckoutRequestDto(Guid CourseId);

public record CheckoutResponseDto(
    string OrderId,
    decimal Amount,
    string Currency,
    string RazorpayKeyId
);

public record VerifyPaymentRequestDto(
    string OrderId,
    string PaymentId,
    string Signature
);

public record PaymentResponseDto(
    Guid Id,
    Guid StudentId,
    Guid CourseId,
    decimal Amount,
    string Currency,
    string Provider,
    string TransactionId,
    string OrderId,
    string Status,
    DateTime CreatedAt,
    DateTime? CompletedAt
);

public record RefundRequestDto(Guid PaymentId, string Reason);

public record WebhookPayloadDto(string Event, WebhookPaymentDto Payment);

public record WebhookPaymentDto(string Id, string OrderId, string Status);

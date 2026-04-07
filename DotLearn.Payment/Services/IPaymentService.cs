using DotLearn.Payment.Models.DTOs;

namespace DotLearn.Payment.Services;

public interface IPaymentService
{
    Task<CheckoutResponseDto> CreateCheckoutAsync(
        CheckoutRequestDto request, Guid studentId);
    Task<PaymentResponseDto> VerifyPaymentAsync(
        VerifyPaymentRequestDto request, Guid studentId);
    Task HandleWebhookAsync(string payload, string signature);
    Task<PaymentResponseDto> RefundAsync(
        RefundRequestDto request, Guid requesterId);
    Task<List<PaymentResponseDto>> GetByStudentIdAsync(Guid studentId);
}

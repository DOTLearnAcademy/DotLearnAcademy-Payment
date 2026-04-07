using System.Security.Cryptography;
using System.Text;

namespace DotLearn.Payment.Services;

public class RazorpaySignatureService
{
    private readonly IConfiguration _config;

    public RazorpaySignatureService(IConfiguration config)
    {
        _config = config;
    }

    public bool VerifyPaymentSignature(
        string orderId, string paymentId, string signature)
    {
        var secret = _config["Razorpay:KeySecret"]!;
        var payload = $"{orderId}|{paymentId}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = BitConverter.ToString(hash)
            .Replace("-", "").ToLower();

        return expected == signature;
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        var secret = _config["Razorpay:WebhookSecret"] ?? 
                     _config["Razorpay:KeySecret"]!;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = BitConverter.ToString(hash)
            .Replace("-", "").ToLower();

        return expected == signature;
    }
}

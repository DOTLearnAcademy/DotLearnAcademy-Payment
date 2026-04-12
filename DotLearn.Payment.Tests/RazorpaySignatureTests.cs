using DotLearn.Payment.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DotLearn.Payment.Tests;

[TestClass]
public class RazorpaySignatureTests
{
    private RazorpaySignatureService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Razorpay:KeySecret"]).Returns("test_secret_key");
        _service = new RazorpaySignatureService(config.Object);
    }

    [TestMethod]
    public void VerifyPaymentSignature_ValidInputs_ReturnsTrue()
    {
        var orderId = "order_123";
        var paymentId = "pay_456";
        var correctSig = ComputeExpectedSignature(orderId, paymentId, "test_secret_key");
        Assert.IsTrue(_service.VerifyPaymentSignature(orderId, paymentId, correctSig));
    }

    [TestMethod]
    public void VerifyPaymentSignature_TamperedSignature_ReturnsFalse()
    {
        Assert.IsFalse(_service.VerifyPaymentSignature("order_123", "pay_456", "tampered_sig"));
    }

    [TestMethod]
    public void VerifyPaymentSignature_EmptyInputs_ReturnsFalse()
    {
        // Empty strings produce a hash that won't match an empty string signature
        Assert.IsFalse(_service.VerifyPaymentSignature("", "", ""));
    }

    private static string ComputeExpectedSignature(string orderId, string paymentId, string secret)
    {
        var payload = $"{orderId}|{paymentId}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }
}

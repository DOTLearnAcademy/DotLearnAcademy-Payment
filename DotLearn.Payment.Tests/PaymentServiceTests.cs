using Amazon.SQS;
using Amazon.SQS.Model;
using DotLearn.Payment.Models.Entities;
using DotLearn.Payment.Repositories;
using DotLearn.Payment.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DotLearn.Payment.Tests;

[TestClass]
public class PaymentServiceTests
{
    private Mock<IPaymentRepository> _repoMock = null!;
    private Mock<IAmazonSQS> _sqsMock = null!;
    private Mock<IConfiguration> _configMock = null!;
    private Mock<IRazorpayOrderService> _razorpayMock = null!;
    private SqsService _sqsService = null!;
    private RazorpaySignatureService _signatureService = null!;
    private IPaymentService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _repoMock = new Mock<IPaymentRepository>();
        _sqsMock = new Mock<IAmazonSQS>();
        _configMock = new Mock<IConfiguration>();
        _razorpayMock = new Mock<IRazorpayOrderService>();

        _configMock.Setup(c => c["SQS:PaymentSucceededQueue"])
            .Returns("https://sqs.ap-southeast-2.amazonaws.com/test/queue");
        _configMock.Setup(c => c["SQS:PaymentFailedQueue"])
            .Returns("https://sqs.ap-southeast-2.amazonaws.com/test/queue-failed");
        _configMock.Setup(c => c["SQS:CourseAccessRevokedQueue"])
            .Returns("https://sqs.ap-southeast-2.amazonaws.com/test/queue2");
        _configMock.Setup(c => c["Razorpay:KeySecret"])
            .Returns("test_secret_key");
        _configMock.Setup(c => c["Razorpay:WebhookSecret"])
            .Returns("test_webhook_secret");

        _sqsMock.Setup(s => s.SendMessageAsync(It.IsAny<SendMessageRequest>(), default))
            .ReturnsAsync(new SendMessageResponse());

        _sqsService = new SqsService(_sqsMock.Object, _configMock.Object);
        _signatureService = new RazorpaySignatureService(_configMock.Object);

        _service = new PaymentService(
            _repoMock.Object,
            _sqsService,
            _signatureService,
            _configMock.Object,
            _razorpayMock.Object);
    }

    [TestMethod]
    public async Task HandleWebhookAsync_DuplicateTransactionId_SilentlyIgnored()
    {
        // Webhook handler checks for duplicate via transactionId inside JSON payload
        var transactionId = "pay_duplicate";
        var orderId = "order_123";

        _repoMock.Setup(r => r.GetByTransactionIdAsync(transactionId))
            .ReturnsAsync(new DotLearn.Payment.Models.Entities.Payment
            {
                TransactionId = transactionId
            });

        // Build a valid Razorpay-style webhook payload
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            @event = "payment.captured",
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = transactionId,
                        order_id = orderId,
                        amount = 49900
                    }
                }
            }
        });

        // Compute a valid webhook signature so signature check passes
        var secret = "test_webhook_secret";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        await _service.HandleWebhookAsync(payload, signature);

        // AddAsync must NOT be called — duplicate, so we return early
        _repoMock.Verify(r => r.AddAsync(It.IsAny<DotLearn.Payment.Models.Entities.Payment>()),
            Times.Never);
    }

    [TestMethod]
    public async Task HandleWebhookAsync_NewPayment_PersistsRecordAndPublishesSqsEvent()
    {
        var transactionId = "pay_new";
        var orderId = "order_456";

        _repoMock.Setup(r => r.GetByTransactionIdAsync(transactionId))
            .ReturnsAsync((DotLearn.Payment.Models.Entities.Payment?)null);
        var pending = new DotLearn.Payment.Models.Entities.Payment { OrderId = orderId, Status = PaymentStatus.Pending };
        _repoMock.Setup(r => r.GetByOrderIdAsync(orderId))
            .ReturnsAsync(pending);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<DotLearn.Payment.Models.Entities.Payment>()))
            .Returns(Task.CompletedTask);

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            @event = "payment.captured",
            payload = new
            {
                payment = new
                {
                    entity = new
                    {
                        id = transactionId,
                        order_id = orderId,
                        amount = 49900
                    }
                }
            }
        });

        var secret = "test_webhook_secret";
        using var hmac = new System.Security.Cryptography.HMACSHA256(
            System.Text.Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        var signature = BitConverter.ToString(hash).Replace("-", "").ToLower();

        await _service.HandleWebhookAsync(payload, signature);

        _repoMock.Verify(r => r.UpdateAsync(
            It.IsAny<DotLearn.Payment.Models.Entities.Payment>()), Times.Once);
        _sqsMock.Verify(s => s.SendMessageAsync(
            It.IsAny<SendMessageRequest>(), default), Times.Once);
    }

    [TestMethod]
    public async Task RefundAsync_SetsStatusRefunded_AndPublishesCourseAccessRevokedEvent()
    {
        var paymentId = Guid.NewGuid();
        var payment = new DotLearn.Payment.Models.Entities.Payment
        {
            Id = paymentId,
            StudentId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            Status = PaymentStatus.Succeeded
        };

        _repoMock.Setup(r => r.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _repoMock.Setup(r => r.UpdateAsync(It.IsAny<DotLearn.Payment.Models.Entities.Payment>()))
            .Returns(Task.CompletedTask);

        await _service.RefundAsync(
            new DotLearn.Payment.Models.DTOs.RefundRequestDto(paymentId, "Test refund"),
            Guid.NewGuid());

        Assert.AreEqual(PaymentStatus.Refunded, payment.Status);
        _sqsMock.Verify(s => s.SendMessageAsync(
            It.IsAny<SendMessageRequest>(), default), Times.Once);
    }

    [TestMethod]
    public async Task CreateCheckoutAsync_SetsPendingTransactionId_BeforePersisting()
    {
        DotLearn.Payment.Models.Entities.Payment? captured = null;

        _repoMock.Setup(r => r.AddAsync(It.IsAny<DotLearn.Payment.Models.Entities.Payment>()))
            .Callback<DotLearn.Payment.Models.Entities.Payment>(p => captured = p)
            .Returns(Task.CompletedTask);

        _configMock.Setup(c => c["Razorpay:KeyId"]).Returns("test_key");
        _configMock.Setup(c => c["Razorpay:KeySecret"]).Returns("test_secret");

        _razorpayMock.Setup(r => r.CreateOrder(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns("fake_order_xyz");

        await _service.CreateCheckoutAsync(new DotLearn.Payment.Models.DTOs.CheckoutRequestDto(Guid.NewGuid()), Guid.NewGuid());

        Assert.IsNotNull(captured);
        Assert.IsNotNull(captured.TransactionId);
        Assert.IsTrue(captured.TransactionId.StartsWith("pending_"));
        Assert.AreEqual("fake_order_xyz", captured.OrderId);
        Assert.AreEqual(PaymentStatus.Pending, captured.Status);
    }
}

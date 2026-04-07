using Amazon.SQS;
using Amazon.SQS.Model;
using DotLearn.Payment.Models.Entities;
using System.Text.Json;

namespace DotLearn.Payment.Services;

public class SqsService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly IConfiguration _config;

    public SqsService(IAmazonSQS sqsClient, IConfiguration config)
    {
        _sqsClient = sqsClient;
        _config = config;
    }

    public async Task PublishPaymentSucceededAsync(Models.Entities.Payment payment)
    {
        var message = JsonSerializer.Serialize(new
        {
            EventType = "PaymentSucceeded",
            StudentId = payment.StudentId,
            CourseId = payment.CourseId,
            TransactionId = payment.TransactionId,
            Amount = payment.Amount,
            Timestamp = DateTime.UtcNow
        });

        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _config["SQS:PaymentSucceededQueue"],
            MessageBody = message
        });
    }

    public async Task PublishPaymentFailedAsync(Models.Entities.Payment payment)
    {
        var message = JsonSerializer.Serialize(new
        {
            EventType = "PaymentFailed",
            StudentId = payment.StudentId,
            CourseId = payment.CourseId,
            OrderId = payment.OrderId,
            Timestamp = DateTime.UtcNow
        });

        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _config["SQS:PaymentFailedQueue"],
            MessageBody = message
        });
    }

    public async Task PublishCourseAccessRevokedAsync(Models.Entities.Payment payment)
    {
        var message = JsonSerializer.Serialize(new
        {
            EventType = "CourseAccessRevoked",
            StudentId = payment.StudentId,
            CourseId = payment.CourseId,
            TransactionId = payment.TransactionId,
            Timestamp = DateTime.UtcNow
        });

        await _sqsClient.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _config["SQS:CourseAccessRevokedQueue"],
            MessageBody = message
        });
    }
}

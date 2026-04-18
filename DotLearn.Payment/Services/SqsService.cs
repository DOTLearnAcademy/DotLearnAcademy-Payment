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

    /// <summary>
    /// Publishes a PaymentSucceeded event to the Enrollment queue so the
    /// Enrollment service can create the student's enrollment record.
    /// </summary>
    public async Task PublishPaymentSucceededAsync(Models.Entities.Payment payment)
    {
        var enrollmentQueueUrl = _config["SQS:EnrollmentPaymentSucceededQueue"];

        if (string.IsNullOrWhiteSpace(enrollmentQueueUrl))
        {
            return; // SQS not configured (e.g. local dev without LocalStack)
        }

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
            QueueUrl = enrollmentQueueUrl,
            MessageBody = message
        });
    }
}

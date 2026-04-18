using DotLearn.Payment.Models.Entities;

namespace DotLearn.Payment.Repositories;

public interface IPaymentRepository
{
    Task<Models.Entities.Payment?> GetByIdAsync(Guid id);
    Task<Models.Entities.Payment?> GetByTransactionIdAsync(string transactionId);
    Task<Models.Entities.Payment?> GetByOrderIdAsync(string orderId);
    Task<Models.Entities.Payment?> GetSuccessfulByStudentAndCourseAsync(Guid studentId, Guid courseId);
    Task<List<Models.Entities.Payment>> GetByStudentIdAsync(Guid studentId);
    Task AddAsync(Models.Entities.Payment payment);
    Task UpdateAsync(Models.Entities.Payment payment);
}

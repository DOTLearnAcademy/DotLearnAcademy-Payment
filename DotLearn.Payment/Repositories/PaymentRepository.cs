using DotLearn.Payment.Data;
using DotLearn.Payment.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotLearn.Payment.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context)
    {
        _context = context;
    }

    public async Task<Models.Entities.Payment?> GetByIdAsync(Guid id) =>
        await _context.Payments.FindAsync(id);

    public async Task<Models.Entities.Payment?> GetByTransactionIdAsync(string transactionId) =>
        await _context.Payments
            .FirstOrDefaultAsync(p => p.TransactionId == transactionId);

    public async Task<Models.Entities.Payment?> GetByOrderIdAsync(string orderId) =>
        await _context.Payments
            .FirstOrDefaultAsync(p => p.OrderId == orderId);

    public async Task<Models.Entities.Payment?> GetSuccessfulByStudentAndCourseAsync(
        Guid studentId, Guid courseId) =>
        await _context.Payments
            .FirstOrDefaultAsync(p =>
                p.StudentId == studentId &&
                p.CourseId == courseId &&
                p.Status == PaymentStatus.Succeeded);

    public async Task<List<Models.Entities.Payment>> GetByStudentIdAsync(Guid studentId) =>
        await _context.Payments
            .Where(p => p.StudentId == studentId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task AddAsync(Models.Entities.Payment payment)
    {
        await _context.Payments.AddAsync(payment);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Models.Entities.Payment payment)
    {
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync();
    }
}

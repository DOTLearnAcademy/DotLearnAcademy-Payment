using System.Text.Json;

namespace DotLearn.Payment.Clients;

public record CoursePriceDto(
    Guid CourseId,
    string Title,
    decimal Price,
    string Currency,
    bool IsPublished,
    bool IsFree
);

public interface ICourseClient
{
    Task<CoursePriceDto> GetCoursePriceAsync(Guid courseId);
}

public class CourseClient : ICourseClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CourseClient> _logger;

    public CourseClient(HttpClient httpClient, ILogger<CourseClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<CoursePriceDto> GetCoursePriceAsync(Guid courseId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/internal/courses/{courseId}/price");
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new KeyNotFoundException($"Course {courseId} not found in Course context.");
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<CoursePriceDto>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                throw new InvalidOperationException("Failed to deserialize CoursePriceDto payload.");
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed while fetching price for Course {CourseId}.", courseId);
            throw new InvalidOperationException($"Service communication failure resolving price for course {courseId}.", ex);
        }
        catch (Exception ex) when (ex is not KeyNotFoundException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Unexpected error fetching price for Course {CourseId}.", courseId);
            throw new InvalidOperationException("An unexpected error occurred while verifying course pricing.");
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PostService.Dtos;

namespace PostService.App;

internal sealed class PostServiceClient : IPostServiceClient
{
    public static IPostServiceClient Shared { get; } = new PostServiceClient();

    private readonly HttpClient client = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("POSTSERVICE_URL") ?? "http://localhost:5036/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private PostServiceClient()
    {
    }

    public async Task<PostingGetDto[]> GetPostingsAsync()
    {
        using var response = await client.GetAsync("/postings");
        return await ReadAsync<PostingGetDto[]>(response);
    }

    public async Task<PostingGetDto> GetPostingByIdAsync(int id)
    {
        using var response = await client.GetAsync($"/postings/{id}");
        return await ReadAsync<PostingGetDto>(response);
    }

    public async Task<PostingGetDto> CreatePostingAsync(PostingPostDto posting)
    {
        using var response = await client.PostAsJsonAsync("/postings", posting, JsonOptions);
        return await ReadAsync<PostingGetDto>(response);
    }

    public async Task<PostingGetDto> UpdatePostingAsync(int id, PostingPutDto posting)
    {
        using var response = await client.PutAsJsonAsync($"/postings/{id}", posting, JsonOptions);
        return await ReadAsync<PostingGetDto>(response);
    }

    public async Task DeletePostingAsync(int id)
    {
        using var response = await client.DeleteAsync($"/postings/{id}");
        await EnsureSuccessAsync(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) where T : class
    {
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new JsonException("PostService returned an empty JSON value.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest &&
            response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<ValidationErrorResponse>(JsonOptions);
                if (problem?.Errors is { Count: > 0 } errors &&
                    errors.Values.All(messages => messages is { Length: > 0 }))
                {
                    throw new PostingValidationException(errors);
                }
            }
            catch (JsonException ex)
            {
                throw new HttpRequestException("PostService returned malformed HTTP 400 details.",
                    ex, response.StatusCode);
            }
        }

        throw new HttpRequestException(
            $"PostService returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private sealed class ValidationErrorResponse
    {
        public Dictionary<string, string[]>? Errors { get; init; }
    }
}

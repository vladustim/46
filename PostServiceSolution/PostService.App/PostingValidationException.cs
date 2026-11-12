namespace PostService.App;

internal sealed class PostingValidationException(Dictionary<string, string[]> errors)
    : Exception("Сервер відхилив дані відправлення.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

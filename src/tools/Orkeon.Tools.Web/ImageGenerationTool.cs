using Orkeon.Constants.Llm;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Base;
using Orkeon.Tools.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Serialization;

namespace Orkeon.Tools.Web;

// ── Typed Request / Response records ──────────────────────────────────────

/// <summary>
/// Strongly-typed request for <see cref="ImageGenerationTool"/>.
/// </summary>
public sealed class ImageGenerationRequest
{
    /// <summary>Gets or sets the text description of the image to generate.</summary>
    [JsonPropertyName("prompt")]
    [FieldSchema(Description = "Text description of the image to generate (max 4000 chars)", Example = "A cute baby sea otter floating on its back")]
    public string Prompt { get; set; } = "";

    /// <summary>Gets or sets the OpenAI API key.</summary>
    [JsonPropertyName("api_key")]
    [FieldSchema(Description = "OpenAI API key (starts with sk-)", Example = "sk-...")]
    public string ApiKey { get; set; } = "";

    /// <summary>Gets or sets the DALL-E model to use.</summary>
    [JsonPropertyName("model")]
    [FieldSchema(Description = "DALL-E model to use", IsRequired = false, Example = "dall-e-3")]
    public string Model { get; set; } = "dall-e-3";

    /// <summary>Gets or sets the image size.</summary>
    [JsonPropertyName("size")]
    [FieldSchema(Description = "Image size (e.g. 1024x1024, 1024x1792, 1792x1024)", IsRequired = false, Example = "1024x1024")]
    public string Size { get; set; } = "1024x1024";

    /// <summary>Gets or sets the image quality.</summary>
    [JsonPropertyName("quality")]
    [FieldSchema(Description = "Image quality: standard or hd", IsRequired = false, Example = "standard")]
    public string Quality { get; set; } = "standard";

    /// <summary>Gets or sets the number of images to generate.</summary>
    [JsonPropertyName("number_of_images")]
    [FieldSchema(Description = "Number of images to generate (1 for dall-e-3, 1-10 for dall-e-2)", IsRequired = false, Example = "1")]
    public int NumberOfImages { get; set; } = 1;

    /// <summary>Gets or sets the response format.</summary>
    [JsonPropertyName("response_format")]
    [FieldSchema(Description = "Response format: url or b64_json", IsRequired = false, Example = "url")]
    public string ResponseFormat { get; set; } = "url";

    /// <summary>Gets or sets the optional path to save the generated image.</summary>
    [JsonPropertyName("save_to_path")]
    [FieldSchema(Description = "Optional file path to save the generated image locally", IsRequired = false)]
    public string? SaveToPath { get; set; }
}

/// <summary>
/// Strongly-typed response for <see cref="ImageGenerationTool"/>.
/// </summary>
public sealed class ImageGenerationResponse
{
    /// <summary>Gets or sets the list of generated images.</summary>
    [JsonPropertyName("images")]
    [ReturnSchema(Description = "List of generated images")]
    public IReadOnlyList<GeneratedImage> Images { get; init; } = [];

    /// <summary>Gets or sets the number of images generated.</summary>
    [JsonPropertyName("image_count")]
    [ReturnSchema(Description = "Number of images generated", Example = 1)]
    public int ImageCount { get; set; }

    /// <summary>Gets or sets the model used for generation.</summary>
    [JsonPropertyName("model")]
    [ReturnSchema(Description = "The model used for generation", Example = "dall-e-3")]
    public string Model { get; set; } = "";
}

/// <summary>
/// Represents a single generated image from the DALL-E API.
/// </summary>
public sealed class GeneratedImage
{
    /// <summary>Gets or sets the URL of the generated image.</summary>
    [JsonPropertyName("url")]
    public Uri? Url { get; set; }

    /// <summary>Gets or sets the Base64-encoded image data.</summary>
    [JsonPropertyName("base64")]
    public string? Base64 { get; set; }

    /// <summary>Gets or sets the revised prompt (DALL-E 3 may rewrite prompts).</summary>
    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; set; }

    /// <summary>Gets or sets the local path where the image was saved.</summary>
    [JsonPropertyName("saved_path")]
    public string? SavedPath { get; set; }
}

// ── Tool implementation ──────────────────────────────────────────────────

/// <summary>
/// Tool for generating images using the OpenAI DALL-E API.
/// Supports DALL-E 2 and DALL-E 3 models with url and b64_json response formats.
/// </summary>
[ToolContract("image_generation",
    Name = "image_generation",
    Description = "Generate images using OpenAI DALL-E API. Supports text-to-image generation with configurable size, quality, and format.",
    Category = "Web Operations")]
public partial class ImageGenerationTool : HttpToolBase<ImageGenerationRequest, ImageGenerationResponse>
{
    private const string OpenAiImagesEndpoint = LlmProviderEndpoints.OpenAI + "/images/generations";
    private readonly IFileSystemService _fileSystem;

    private static readonly HashSet<string> ValidSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "256x256", "512x512", "1024x1024", "1024x1792", "1792x1024"
    };

    private static readonly HashSet<string> ValidQualities = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard", "hd"
    };

    private static readonly HashSet<string> ValidResponseFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "b64_json"
    };

    /// <summary>
    /// Initializes a new instance of <see cref="ImageGenerationTool"/> with virtual file system support.
    /// </summary>
    /// <param name="fileSystem">Virtual file system service for sandboxed write operations.</param>
    /// <param name="httpClient">Optional pre-configured <see cref="HttpClient"/>.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ImageGenerationTool(
        IFileSystemService fileSystem,
        HttpClient? httpClient = null,
        ILogger<ImageGenerationTool>? logger = null)
        : base(httpClient, logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ImageGenerationTool"/> with virtual file system support
    /// and SSRF protection. The remote image URL returned by the DALL-E API is validated before it is
    /// downloaded (defense against a poisoned/redirected image URL pointing at an internal address).
    /// </summary>
    /// <param name="fileSystem">Virtual file system service for sandboxed write operations.</param>
    /// <param name="urlValidator">URL validator for SSRF protection of the downloaded image URL.</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="httpClient">Optional pre-configured <see cref="HttpClient"/>.</param>
    /// <param name="logger">Optional logger instance.</param>
    public ImageGenerationTool(
        IFileSystemService fileSystem,
        IUrlValidator urlValidator,
        HttpHeaderSanitizer headerSanitizer,
        HttpClient? httpClient = null,
        ILogger<ImageGenerationTool>? logger = null)
        : base(urlValidator, headerSanitizer, httpClient, logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    }

    /// <inheritdoc />
    protected override string? ValidateTypedRequest(ImageGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return "Prompt cannot be empty.";

        if (request.Prompt.Length > 4000)
            return "Prompt cannot exceed 4000 characters.";

        if (string.IsNullOrWhiteSpace(request.ApiKey))
            return "API key is required.";

        if (!request.ApiKey.StartsWith("sk-", StringComparison.Ordinal))
            return "Invalid API key format. OpenAI API keys start with 'sk-'.";

        if (!ValidSizes.Contains(request.Size))
            return $"Invalid size '{request.Size}'. Allowed sizes: {string.Join(", ", ValidSizes)}.";

        if (!ValidQualities.Contains(request.Quality))
            return $"Invalid quality '{request.Quality}'. Allowed values: standard, hd.";

        if (!ValidResponseFormats.Contains(request.ResponseFormat))
            return $"Invalid response format '{request.ResponseFormat}'. Allowed values: url, b64_json.";

        if (request.NumberOfImages < 1 || request.NumberOfImages > 10)
            return "Number of images must be between 1 and 10.";

        if (string.Equals(request.Model, "dall-e-3", StringComparison.OrdinalIgnoreCase) && request.NumberOfImages > 1)
            return "DALL-E 3 only supports generating 1 image at a time. Use dall-e-2 for multiple images.";

        if (request.SaveToPath is not null)
            return ValidateSaveToPath(request.SaveToPath);

        return null;
    }

    /// <summary>
    /// Validates the optional <c>save_to_path</c> through the VFS policy.
    /// </summary>
    private string? ValidateSaveToPath(string saveToPath)
    {
        // Early VFS-level path check: catches directory traversal / out-of-mount
        // SaveToPath values before any HTTP call is issued.
        var pathCheck = _fileSystem.ResolveAndValidate(saveToPath, FileAccessRights.Write);
        if (!pathCheck.IsAllowed)
        {
            var reason = pathCheck.DenialReason ?? "rejected by file system policy";
            return $"Invalid save_to_path: {reason}";
        }

        return null;
    }

    /// <inheritdoc />
    protected override Task<ImageGenerationResponse> ExecuteTypedAsync(
        ImageGenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteTypedCoreAsync();

        async Task<ImageGenerationResponse> ExecuteTypedCoreAsync()
        {
            // Build the JSON payload
            var payload = new Dictionary<string, object>
            {
                ["model"] = request.Model,
                ["prompt"] = request.Prompt,
                ["n"] = request.NumberOfImages,
                ["size"] = request.Size,
                ["response_format"] = request.ResponseFormat
            };

            if (string.Equals(request.Model, "dall-e-3", StringComparison.OrdinalIgnoreCase))
            {
                payload["quality"] = request.Quality;
            }

            var jsonPayload = JsonSerializer.Serialize(payload);

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, OpenAiImagesEndpoint)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("Authorization", $"Bearer {request.ApiKey}");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = ParseOpenAiError(responseBody, (int)response.StatusCode);
                throw new HttpRequestException(errorMessage);
            }

            var apiResponse = JsonSerializer.Deserialize<OpenAiImageResponse>(responseBody, JsonOptions);

            var images = new List<GeneratedImage>();

            if (apiResponse?.Data is not null)
            {
                var data = apiResponse.Data;
                for (var i = 0; i < data.Count; i++)
                {
                    var image = MapImage(data[i]);
                    await SaveImageIfRequestedAsync(image, request.SaveToPath, i, data.Count, cancellationToken)
                        .ConfigureAwait(false);
                    images.Add(image);
                }
            }

            LogImageGenerated(request.Prompt, images.Count, request.Model);

            return new ImageGenerationResponse
            {
                Images = images,
                ImageCount = images.Count,
                Model = request.Model
            };
        }
    }

    private static string ParseOpenAiError(string responseBody, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var errorObj = doc.RootElement.GetProperty("error");
            var code = errorObj.TryGetProperty("code", out var codeElement)
                ? codeElement.GetString() ?? ""
                : "";
            var message = errorObj.TryGetProperty("message", out var msgElement)
                ? msgElement.GetString() ?? ""
                : "";

            return code switch
            {
                "content_policy_violation" =>
                    $"Content policy violation: Your prompt was rejected by OpenAI's safety system. {message}",
                "rate_limit_exceeded" =>
                    $"Rate limit exceeded. Please wait a moment and try again. {message}",
                "invalid_api_key" =>
                    $"Invalid API key. Please check your OpenAI API key and try again.",
                "billing_hard_limit_reached" =>
                    $"Billing quota exceeded. Please check your OpenAI account billing settings. {message}",
                _ => $"OpenAI API error ({statusCode}): {message}"
            };
        }
        catch (JsonException)
        {
            return $"OpenAI API error ({statusCode}): {responseBody}";
        }
    }

    private static GeneratedImage MapImage(OpenAiImageData imageData) => new()
    {
        Url = Uri.TryCreate(imageData.Url, UriKind.Absolute, out var u) ? u : null,
        Base64 = imageData.B64Json,
        RevisedPrompt = imageData.RevisedPrompt
    };

    /// <summary>
    /// Saves <paramref name="image"/> when a <paramref name="saveToPath"/> was requested, resolving
    /// the (possibly index-suffixed) target path and re-validating it through the VFS policy.
    /// </summary>
    private async Task SaveImageIfRequestedAsync(
        GeneratedImage image,
        string? saveToPath,
        int index,
        int total,
        CancellationToken cancellationToken)
    {
        if (saveToPath is null)
            return;

        var savePath = total > 1
            ? AppendIndexToPath(saveToPath, index)
            : saveToPath;

        // Re-validate the final, possibly index-suffixed path through the VFS
        // policy before each write. Symmetric with the pre-execute check in
        // ValidateTypedRequest and required for multi-image runs.
        var pathCheck = _fileSystem.ResolveAndValidate(savePath, FileAccessRights.Write);
        if (!pathCheck.IsAllowed)
        {
            var reason = pathCheck.DenialReason ?? "rejected by file system policy";
            throw new UnauthorizedAccessException($"Invalid save_to_path: {reason}");
        }

        await SaveImageAsync(image, savePath, cancellationToken).ConfigureAwait(false);
        image.SavedPath = savePath;
    }

    private async Task SaveImageAsync(GeneratedImage image, string path, CancellationToken cancellationToken)
    {
        if (image.Base64 is not null)
        {
            var bytes = Convert.FromBase64String(image.Base64);
            await _fileSystem.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        else if (image.Url is not null)
        {
            // SSRF protection: the image URL is provider-returned data; validate it
            // (or its resolved IP) before downloading so a poisoned/redirected URL
            // cannot reach an internal address.
            var urlValidation = await ValidateUrlAsync(image.Url, cancellationToken).ConfigureAwait(false);
            if (!urlValidation.IsAllowed)
                throw new InvalidOperationException($"Image URL blocked: {urlValidation.DenialReason}");

            var imageBytes = await _httpClient.GetByteArrayAsync(urlValidation.ValidatedUri!, cancellationToken).ConfigureAwait(false);
            await _fileSystem.WriteAllBytesAsync(path, imageBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string AppendIndexToPath(string path, int index)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, $"{name}_{index}{ext}");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = SerializationDefaults.JsonMaxDepth
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated {Count} image(s) for prompt '{Prompt}' using model {Model}")]
    private partial void LogImageGenerated(string prompt, int count, string model);

    // ── OpenAI API response models (internal) ────────────────────────────

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the OpenAI image API response.")]
    private sealed class OpenAiImageResponse
    {
        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("data")]
        public List<OpenAiImageData>? Data { get; set; }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812", Justification = "Instantiated via System.Text.Json deserialization of the OpenAI image API response.")]
    private sealed class OpenAiImageData
    {
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("b64_json")]
        public string? B64Json { get; set; }

        [JsonPropertyName("revised_prompt")]
        public string? RevisedPrompt { get; set; }
    }
}

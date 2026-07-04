namespace Orkeon.Application.Interfaces.Infrastructure;

/// <summary>
/// Abstraction for HTTP client operations to avoid infrastructure dependencies in the domain layer.
/// </summary>
public interface IHttpClient
{
    /// <summary>
    /// Sends a GET request and deserializes the response.
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to.</typeparam>
    /// <param name="url">The URL to send the request to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    System.Threading.Tasks.Task<T> GetAsync<T>(Uri url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a POST request with data and deserializes the response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data.</typeparam>
    /// <typeparam name="TResponse">The type to deserialize the response to.</typeparam>
    /// <param name="url">The URL to send the request to.</param>
    /// <param name="data">The data to send in the request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    System.Threading.Tasks.Task<TResponse> PostAsync<TRequest, TResponse>(Uri url, TRequest data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a PUT request with data and deserializes the response.
    /// </summary>
    /// <typeparam name="TRequest">The type of the request data.</typeparam>
    /// <typeparam name="TResponse">The type to deserialize the response to.</typeparam>
    /// <param name="url">The URL to send the request to.</param>
    /// <param name="data">The data to send in the request body.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized response.</returns>
    System.Threading.Tasks.Task<TResponse> PutAsync<TRequest, TResponse>(Uri url, TRequest data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a DELETE request.
    /// </summary>
    /// <param name="url">The URL to send the request to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    System.Threading.Tasks.Task DeleteAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a GET request and returns the response as a string.
    /// </summary>
    /// <param name="url">The URL to send the request to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response body as a string.</returns>
    System.Threading.Tasks.Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken = default);
}

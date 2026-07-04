using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.Runtime;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IAmazonSecretsManager for testing.
/// Only implements the methods used in tests; others throw NotImplementedException.
/// </summary>
public sealed class MockAmazonSecretsManager : IAmazonSecretsManager
{
    private Func<GetSecretValueRequest, CancellationToken, Task<GetSecretValueResponse>>? _getSecretValueFunc;
    private Func<DescribeSecretRequest, CancellationToken, Task<DescribeSecretResponse>>? _describeSecretFunc;
    private Func<ListSecretsRequest, CancellationToken, Task<ListSecretsResponse>>? _listSecretsFunc;

    // --- Tracking ---
    public int GetSecretValueCallCount { get; private set; }
    public int DescribeSecretCallCount { get; private set; }
    public int ListSecretsCallCount { get; private set; }
    public GetSecretValueRequest? LastGetSecretValueRequest { get; private set; }
    public DescribeSecretRequest? LastDescribeSecretRequest { get; private set; }

    // --- Configuration ---
    public void SetGetSecretValueFunc(Func<GetSecretValueRequest, CancellationToken, Task<GetSecretValueResponse>> func)
        => _getSecretValueFunc = func;

    public void SetGetSecretValueResult(string secretId, string secretString)
    {
        _getSecretValueFunc = (req, ct) =>
        {
            if (req.SecretId == secretId)
                return Task.FromResult(new GetSecretValueResponse { SecretString = secretString });
            throw new ResourceNotFoundException("not found");
        };
    }

    public void SetGetSecretValueThrows(string secretId, Exception ex)
    {
        _getSecretValueFunc = (req, ct) =>
        {
            if (req.SecretId == secretId)
                throw ex;
            throw new ResourceNotFoundException("not found");
        };
    }

    public void SetDescribeSecretFunc(Func<DescribeSecretRequest, CancellationToken, Task<DescribeSecretResponse>> func)
        => _describeSecretFunc = func;

    public void SetListSecretsResult(ListSecretsResponse response)
    {
        _listSecretsFunc = (req, ct) => Task.FromResult(response);
    }

    // --- IAmazonSecretsManager implementation ---
    public Task<GetSecretValueResponse> GetSecretValueAsync(GetSecretValueRequest request, CancellationToken cancellationToken = default)
    {
        GetSecretValueCallCount++;
        LastGetSecretValueRequest = request;
        if (_getSecretValueFunc != null)
            return _getSecretValueFunc(request, cancellationToken);
        throw new NotImplementedException("GetSecretValueAsync not configured");
    }

    public Task<DescribeSecretResponse> DescribeSecretAsync(DescribeSecretRequest request, CancellationToken cancellationToken = default)
    {
        DescribeSecretCallCount++;
        LastDescribeSecretRequest = request;
        if (_describeSecretFunc != null)
            return _describeSecretFunc(request, cancellationToken);
        throw new NotImplementedException("DescribeSecretAsync not configured");
    }

    public Task<ListSecretsResponse> ListSecretsAsync(ListSecretsRequest request, CancellationToken cancellationToken = default)
    {
        ListSecretsCallCount++;
        if (_listSecretsFunc != null)
            return _listSecretsFunc(request, cancellationToken);
        throw new NotImplementedException("ListSecretsAsync not configured");
    }

    // --- Remaining interface members (not used in tests) ---
    public ISecretsManagerPaginatorFactory Paginators => throw new NotImplementedException();
    public IClientConfig Config => throw new NotImplementedException();

    public Task<BatchGetSecretValueResponse> BatchGetSecretValueAsync(BatchGetSecretValueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CancelRotateSecretResponse> CancelRotateSecretAsync(CancelRotateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<CreateSecretResponse> CreateSecretAsync(CreateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteResourcePolicyResponse> DeleteResourcePolicyAsync(DeleteResourcePolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<DeleteSecretResponse> DeleteSecretAsync(DeleteSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetRandomPasswordResponse> GetRandomPasswordAsync(GetRandomPasswordRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<GetResourcePolicyResponse> GetResourcePolicyAsync(GetResourcePolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ListSecretVersionIdsResponse> ListSecretVersionIdsAsync(ListSecretVersionIdsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PutResourcePolicyResponse> PutResourcePolicyAsync(PutResourcePolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<PutSecretValueResponse> PutSecretValueAsync(PutSecretValueRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RemoveRegionsFromReplicationResponse> RemoveRegionsFromReplicationAsync(RemoveRegionsFromReplicationRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ReplicateSecretToRegionsResponse> ReplicateSecretToRegionsAsync(ReplicateSecretToRegionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RestoreSecretResponse> RestoreSecretAsync(RestoreSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<RotateSecretResponse> RotateSecretAsync(RotateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<StopReplicationToReplicaResponse> StopReplicationToReplicaAsync(StopReplicationToReplicaRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<TagResourceResponse> TagResourceAsync(TagResourceRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UntagResourceResponse> UntagResourceAsync(UntagResourceRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UpdateSecretResponse> UpdateSecretAsync(UpdateSecretRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<UpdateSecretVersionStageResponse> UpdateSecretVersionStageAsync(UpdateSecretVersionStageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<ValidateResourcePolicyResponse> ValidateResourcePolicyAsync(ValidateResourcePolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Amazon.Runtime.Endpoints.Endpoint DetermineServiceOperationEndpoint(AmazonWebServiceRequest request) => throw new NotImplementedException();
    public void Dispose() { }
}

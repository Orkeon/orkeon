using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>Returns a fixed validation result and records that it was called.</summary>
public sealed class StubCitationBlockValidator : ICitationBlockValidator
{
    private readonly ValidationResult _result;
    public bool WasCalled { get; private set; }

    public StubCitationBlockValidator(ValidationResult result) => _result = result;

    public Task<ValidationResult> ValidateAsync(string fileContent, CancellationToken ct)
    {
        WasCalled = true;
        return Task.FromResult(_result);
    }
}

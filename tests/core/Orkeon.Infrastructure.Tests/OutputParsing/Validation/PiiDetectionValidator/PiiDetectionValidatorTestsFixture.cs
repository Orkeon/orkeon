using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class PiiDetectionValidatorTestsFixture
{
    private readonly PiiDetectionValidator _validator = new();

    public PiiDetectionValidatorTestsFixture()
    {
    }

    public PiiDetectionValidator GetValidator() => _validator;

}

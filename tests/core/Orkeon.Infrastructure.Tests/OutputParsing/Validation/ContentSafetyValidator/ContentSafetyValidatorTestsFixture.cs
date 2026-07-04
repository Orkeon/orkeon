using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class ContentSafetyValidatorTestsFixture
{
    private readonly ContentSafetyValidator _validator = new();

    public ContentSafetyValidatorTestsFixture()
    {
    }

    public ContentSafetyValidator GetValidator() => _validator;

}

using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class LengthValidatorTestsFixture
{
    private readonly LengthValidator _validator = new();

    public LengthValidatorTestsFixture()
    {
    }

    public LengthValidator GetValidator() => _validator;

}

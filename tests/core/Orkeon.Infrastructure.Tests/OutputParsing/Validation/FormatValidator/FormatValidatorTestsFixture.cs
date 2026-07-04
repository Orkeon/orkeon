using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class FormatValidatorTestsFixture
{
    private readonly FormatValidator _validator = new();

    public FormatValidatorTestsFixture()
    {
    }

    public FormatValidator GetValidator() => _validator;

}

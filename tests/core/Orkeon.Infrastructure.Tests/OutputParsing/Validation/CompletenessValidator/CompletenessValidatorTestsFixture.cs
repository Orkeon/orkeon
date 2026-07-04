using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class CompletenessValidatorTestsFixture
{
    private readonly CompletenessValidator _validator = new();

    public CompletenessValidatorTestsFixture()
    {
    }

    public CompletenessValidator GetValidator() => _validator;

}

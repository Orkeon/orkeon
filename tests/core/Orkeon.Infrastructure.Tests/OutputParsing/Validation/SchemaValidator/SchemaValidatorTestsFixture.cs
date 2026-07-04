using Orkeon.Infrastructure.OutputParsing.Validation;

namespace Orkeon.Infrastructure.Tests.OutputParsing.Validation;

public class SchemaValidatorTestsFixture
{
    private readonly SchemaValidator _validator = new();

    public SchemaValidatorTestsFixture()
    {
    }

    public SchemaValidator GetValidator() => _validator;

}

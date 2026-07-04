using Microsoft.Extensions.Logging;
using Orkeon.Application.Tests.Fixtures;

namespace Orkeon.Application.Tests.TestHelpers;

public static class TestLoggerExtensions
{
    public static bool HasLoggedMessage<T>(this TestLogger<T> logger, LogLevel level, string messageContains)
    {
        return logger.LogEntries.Any(m =>
            m.LogLevel == level &&
            m.Message?.Contains(messageContains) == true);
    }
}

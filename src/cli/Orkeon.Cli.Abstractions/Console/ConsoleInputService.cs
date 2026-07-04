namespace Orkeon.Cli.Abstractions.Console;

/// <summary>
/// Helpers for prompting standardized input through an <see cref="IConsoleAdapter"/>.
/// </summary>
public class ConsoleInputService
{
    private readonly IConsoleAdapter _console;

    public ConsoleInputService(IConsoleAdapter console)
    {
        _console = console;
    }

    public int GetMenuChoice(int min, int max)
    {
        while (true)
        {
            if (int.TryParse(_console.ReadLine(), out int choice)
                && choice >= min && choice <= max)
            {
                return choice;
            }

            _console.Write($"Invalid choice. Please enter a number between {min} and {max}: ");
        }
    }

    public string GetRequiredString(string prompt)
    {
        while (true)
        {
            _console.Write(prompt);
            var input = _console.ReadLine()?.Trim();

            if (!string.IsNullOrEmpty(input))
            {
                return input;
            }

            _console.WriteLine("This field is required. Please try again.");
        }
    }

    public string? GetOptionalString(string prompt)
    {
        _console.Write(prompt);
        var input = _console.ReadLine()?.Trim();
        return string.IsNullOrEmpty(input) ? null : input;
    }

    public bool GetYesNo(string prompt)
    {
        _console.Write($"{prompt} (y/n): ");

        while (true)
        {
            var key = _console.ReadKey(true).Key;

            if (key == ConsoleKey.Y)
            {
                _console.WriteLine("Yes");
                return true;
            }
            else if (key == ConsoleKey.N)
            {
                _console.WriteLine("No");
                return false;
            }
        }
    }

    public IReadOnlyList<string> GetMultipleChoices(string prompt, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _console.WriteLine(prompt);
        _console.WriteLine("Available options:");

        for (int i = 0; i < options.Count; i++)
        {
            _console.WriteLine($"  {i + 1}. {options[i]}");
        }

        _console.WriteLine("\nEnter numbers separated by commas (e.g., 1,3,5) or press Enter for none:");

        var input = _console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(input))
        {
            return [];
        }

        var selected = new List<string>();
        var numbers = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var number in numbers)
        {
            if (int.TryParse(number, out int index) && index > 0 && index <= options.Count)
            {
                selected.Add(options[index - 1]);
            }
        }

        return selected;
    }

    public void WaitForKey(string message = "Press any key to continue...")
    {
        _console.WriteLine(message);
        _console.ReadKey(true);
    }
}

namespace Orkeon.Application.Configuration;

/// <summary>
/// Marks a list property of a declared settings shape whose reader also takes the list as one value,
/// its items separated by commas (<c>Llm__AvailableModels=gpt-4o,claude</c>): the form an environment
/// variable can write. The start-time check of the keys then accepts a single value at that key; it
/// refuses one at every other list, which the binder would bind to an empty list without a word (#454).
/// Only a reader that splits the value itself carries it.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CommaSeparatedSettingAttribute : Attribute
{
}

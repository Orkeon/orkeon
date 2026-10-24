namespace Orkeon.Analysis.Core.Editing;

internal interface IEditOperation
{
    EditResult Apply(ReadOnlyMemory<byte> current, StructuralFile parsed);
}

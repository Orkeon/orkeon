// CSV parser: splits a row on commas and trims each field.
export function parseCsvRow(line: string): string[] {
    return line.split(",").map(cell => cell.trim());
}

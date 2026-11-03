namespace ImageScanner.Core.Library;

/// <summary>
/// Compares strings so that embedded numbers sort numerically ("page2" before "page10").
/// </summary>
public sealed class NaturalStringComparer : IComparer<string?> {
    public static NaturalStringComparer Instance { get; } = new();

    public int Compare(string? left, string? right) {
        if (ReferenceEquals(left, right)) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        int leftIndex = 0;
        int rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length) {
            if (char.IsDigit(left[leftIndex]) && char.IsDigit(right[rightIndex])) {
                int leftStart = leftIndex;
                int rightStart = rightIndex;
                while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
                while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;

                ReadOnlySpan<char> leftNumber = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
                ReadOnlySpan<char> rightNumber = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');
                if (leftNumber.Length != rightNumber.Length) {
                    return leftNumber.Length.CompareTo(rightNumber.Length);
                }

                int numberComparison = leftNumber.CompareTo(rightNumber, StringComparison.Ordinal);
                if (numberComparison != 0) {
                    return numberComparison;
                }
            }
            else {
                int charComparison = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
                if (charComparison != 0) {
                    return charComparison;
                }

                leftIndex++;
                rightIndex++;
            }
        }

        return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);
    }
}

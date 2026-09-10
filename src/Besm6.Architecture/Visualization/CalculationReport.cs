using System.Text;

namespace Besm6.Architecture.Visualization;

/// <summary>Пошаговый учебный отчёт одного вычисления.</summary>
public sealed class CalculationReport
{
    private readonly string[] _steps;

    public CalculationReport(string operation, string input, string result, IEnumerable<string> steps)
    {
        if (string.IsNullOrWhiteSpace(operation))
            throw new ArgumentException("Название операции не может быть пустым.", nameof(operation));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(steps);

        Operation = operation;
        Input = input;
        Result = result;
        _steps = steps.ToArray();
        if (_steps.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Шаги отчёта не могут быть пустыми.", nameof(steps));
    }

    public string Operation { get; }

    public string Input { get; }

    public string Result { get; }

    public IReadOnlyList<string> Steps => _steps;

    public string Render()
    {
        var text = new StringBuilder()
            .AppendLine(Operation)
            .Append("Вход: ").AppendLine(Input);

        for (int index = 0; index < _steps.Length; index++)
            text.Append(index + 1).Append(". ").AppendLine(_steps[index]);

        return text.Append("Результат: ").Append(Result).ToString();
    }
}

using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace DaisyOS.Shell.Apps.Calculator;

public enum AngleUnit
{
    Deg,
    Rad,
    Gra
}

public enum CalculatorMode
{
    Standard,
    Scientific
}

public class CalculatorViewModel : INotifyPropertyChanged, IDisposable
{
    private string _display = "0";
    private string _expression = string.Empty;
    private double _accumulator = 0;
    private string? _pendingOperator = null;
    private bool _isNewEntry = true;
    private bool _isInverse = false;
    private bool _isHyp = false;
    private AngleUnit _angleUnit = AngleUnit.Deg;
    private double _memory = 0;
    private bool _hasMemory = false;
    private CalculatorMode _currentMode = CalculatorMode.Standard;

    public string Display
    {
        get => _display == "Error" ? Loc.Get("Error") : _display;
        set
        {
            if (_display != value)
            {
                _display = value;
                OnPropertyChanged();
            }
        }
    }

    public string Expression
    {
        get => _expression;
        set
        {
            if (_expression != value)
            {
                _expression = value;
                OnPropertyChanged();
            }
        }
    }

    public CalculatorMode Mode
    {
        get => _currentMode;
        set
        {
            if (_currentMode != value)
            {
                _currentMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ModeTitle));
                OnPropertyChanged(nameof(IsScientific));
                OnPropertyChanged(nameof(ModeToggleIcon));
                OnPropertyChanged(nameof(ModeToggleTip));
            }
        }
    }

    public string ModeTitle => Loc.Get(_currentMode == CalculatorMode.Standard ? "Standard" : "Scientific");
    public bool IsScientific => _currentMode == CalculatorMode.Scientific;

    // Semantic SVG names: standard mode offers functions; scientific
    // mode returns to the familiar calculator keypad.
    public string ModeToggleIcon => _currentMode == CalculatorMode.Standard ? "function" : "calculator";
    public string ModeToggleTip => Loc.Get("Toggle Standard / Scientific");

    public bool IsInverse
    {
        get => _isInverse;
        set
        {
            if (_isInverse != value)
            {
                _isInverse = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsHyp
    {
        get => _isHyp;
        set
        {
            if (_isHyp != value)
            {
                _isHyp = value;
                OnPropertyChanged();
            }
        }
    }

    public AngleUnit AngleMode
    {
        get => _angleUnit;
        set
        {
            if (_angleUnit != value)
            {
                _angleUnit = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AngleModeText));
            }
        }
    }

    public string AngleModeText => _angleUnit.ToString().ToUpperInvariant();

    public bool HasMemory
    {
        get => _hasMemory;
        set
        {
            if (_hasMemory != value)
            {
                _hasMemory = value;
                OnPropertyChanged();
            }
        }
    }

    public CalculatorViewModel()
    {
        Loc.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ModeTitle));
        OnPropertyChanged(nameof(ModeToggleTip));
        OnPropertyChanged(nameof(Display));
    }

    public void ToggleMode()
    {
        Mode = Mode == CalculatorMode.Standard ? CalculatorMode.Scientific : CalculatorMode.Standard;
    }

    public void InputDigit(string digit)
    {
        if (_isNewEntry || _display == "0" || _display == "Error")
        {
            _display = digit == "." ? "0." : digit;
            _isNewEntry = false;
        }
        else
        {
            if (digit == "." && _display.Contains('.'))
            {
                return;
            }
            if (_display.Length < 16)
            {
                _display += digit;
            }
        }
        OnPropertyChanged(nameof(Display));
    }

    public void InputOperator(string op)
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var currentVal))
        {
            if (_pendingOperator != null && !_isNewEntry)
            {
                currentVal = EvaluatePending(_accumulator, currentVal, _pendingOperator);
                Display = FormatResult(currentVal);
            }

            _accumulator = currentVal;
            _pendingOperator = op;
            Expression = $"{FormatResult(_accumulator)} {op}";
            _isNewEntry = true;
        }
    }

    public void ExecuteEquals()
    {
        if (_pendingOperator != null && double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var currentVal))
        {
            Expression = $"{FormatResult(_accumulator)} {_pendingOperator} {FormatResult(currentVal)} =";
            var result = EvaluatePending(_accumulator, currentVal, _pendingOperator);
            Display = FormatResult(result);
            _accumulator = result;
            _pendingOperator = null;
            _isNewEntry = true;
        }
    }

    public void ClearEntry()
    {
        Display = "0";
        _isNewEntry = true;
    }

    public void AllClear()
    {
        Display = "0";
        Expression = string.Empty;
        _accumulator = 0;
        _pendingOperator = null;
        _isNewEntry = true;
        IsInverse = false;
        IsHyp = false;
    }

    public void Backspace()
    {
        if (_isNewEntry || _display == "Error" || _display.Length <= 1)
        {
            Display = "0";
            _isNewEntry = true;
            return;
        }

        Display = _display[..^1];
        if (string.IsNullOrEmpty(_display) || _display == "-")
        {
            Display = "0";
            _isNewEntry = true;
        }
    }

    public void ToggleSign()
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            Display = FormatResult(-val);
        }
    }

    public void CalculatePercentage()
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            double res = _pendingOperator != null ? _accumulator * (val / 100.0) : val / 100.0;
            Display = FormatResult(res);
            _isNewEntry = true;
        }
    }

    public void ApplyScientificFunction(string func)
    {
        if (!double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            return;
        }

        double result = 0;
        bool error = false;

        switch (func.ToLowerInvariant())
        {
            case "sin":
                var angleSin = ToRadians(val);
                result = _isHyp ? (_isInverse ? Math.Asinh(val) : Math.Sinh(val)) : (_isInverse ? Math.Asin(val) : Math.Sin(angleSin));
                if (_isInverse && !_isHyp) result = FromRadians(result);
                break;
            case "cos":
                var angleCos = ToRadians(val);
                result = _isHyp ? (_isInverse ? Math.Acosh(val) : Math.Cosh(val)) : (_isInverse ? Math.Acos(val) : Math.Cos(angleCos));
                if (_isInverse && !_isHyp) result = FromRadians(result);
                break;
            case "tan":
                var angleTan = ToRadians(val);
                result = _isHyp ? (_isInverse ? Math.Atanh(val) : Math.Tanh(val)) : (_isInverse ? Math.Atan(val) : Math.Tan(angleTan));
                if (_isInverse && !_isHyp) result = FromRadians(result);
                break;
            case "sqrt":
                if (_isInverse)
                {
                    result = val * val; // x²
                }
                else
                {
                    if (val < 0) error = true;
                    else result = Math.Sqrt(val);
                }
                break;
            case "sqr":
                result = val * val;
                break;
            case "log":
                if (_isInverse)
                {
                    result = Math.Pow(10, val); // 10ˣ
                }
                else
                {
                    if (val <= 0) error = true;
                    else result = Math.Log10(val);
                }
                break;
            case "ln":
                if (_isInverse)
                {
                    result = Math.Exp(val); // eˣ
                }
                else
                {
                    if (val <= 0) error = true;
                    else result = Math.Log(val);
                }
                break;
            case "recip": // 1/x
                if (val == 0) error = true;
                else result = 1.0 / val;
                break;
            case "fact": // x!
                if (val < 0 || val != Math.Floor(val) || val > 170) error = true;
                else result = Factorial((int)val);
                break;
            case "pi":
                result = Math.PI;
                break;
            default:
                return;
        }

        if (error || double.IsNaN(result) || double.IsInfinity(result))
        {
            Display = "Error";
        }
        else
        {
            Display = FormatResult(result);
        }

        _isNewEntry = true;
        IsInverse = false;
        IsHyp = false;
    }

    public void ToggleInverse() => IsInverse = !IsInverse;
    public void ToggleHyp() => IsHyp = !IsHyp;

    public void CycleAngleUnit()
    {
        AngleMode = AngleMode switch
        {
            AngleUnit.Deg => AngleUnit.Rad,
            AngleUnit.Rad => AngleUnit.Gra,
            _ => AngleUnit.Deg
        };
    }

    public void MemoryClear()
    {
        _memory = 0;
        HasMemory = false;
    }

    public void MemoryRecall()
    {
        Display = FormatResult(_memory);
        _isNewEntry = true;
    }

    public void MemoryAdd()
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            _memory += val;
            HasMemory = _memory != 0;
            _isNewEntry = true;
        }
    }

    public void MemorySubtract()
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            _memory -= val;
            HasMemory = _memory != 0;
            _isNewEntry = true;
        }
    }

    public void MemoryStore()
    {
        if (double.TryParse(_display, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
        {
            _memory = val;
            HasMemory = _memory != 0;
            _isNewEntry = true;
        }
    }

    private double EvaluatePending(double left, double right, string op)
    {
        return op switch
        {
            "+" => left + right,
            "-" => left - right,
            "×" or "*" => left * right,
            "÷" or "/" => right != 0 ? left / right : double.NaN,
            "^" => Math.Pow(left, right),
            "%" => left * (right / 100.0),
            _ => right
        };
    }

    private double ToRadians(double value)
    {
        return _angleUnit switch
        {
            AngleUnit.Deg => value * (Math.PI / 180.0),
            AngleUnit.Gra => value * (Math.PI / 200.0),
            _ => value
        };
    }

    private double FromRadians(double rad)
    {
        return _angleUnit switch
        {
            AngleUnit.Deg => rad * (180.0 / Math.PI),
            AngleUnit.Gra => rad * (200.0 / Math.PI),
            _ => rad
        };
    }

    private static double Factorial(int n)
    {
        if (n <= 1) return 1;
        double res = 1;
        for (int i = 2; i <= n; i++) res *= i;
        return res;
    }

    private static string FormatResult(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "Error";
        string formatted = value.ToString("G10", CultureInfo.InvariantCulture);
        return formatted;
    }

    public void Dispose()
    {
        Loc.Changed -= OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

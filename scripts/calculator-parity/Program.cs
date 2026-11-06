using System.Text.Json;
using DaisyOS.Shell.Apps.Calculator;
string? line;
while ((line=Console.ReadLine()) is not null)
{
    using var vm=new CalculatorViewModel();
    foreach(var key in JsonSerializer.Deserialize<string[]>(line)!)
    {
        switch(key)
        {
            case "+": case "-": case "*": case "×": case "/": case "÷": case "^":vm.InputOperator(key);break;
            case "=":vm.ExecuteEquals();break;case "AC":vm.AllClear();break;case "CE":vm.ClearEntry();break;
            case "backspace":vm.Backspace();break;case "sign":vm.ToggleSign();break;case "%":vm.CalculatePercentage();break;
            case "mode":vm.ToggleMode();break;case "inv":vm.ToggleInverse();break;case "hyp":vm.ToggleHyp();break;case "angle":vm.CycleAngleUnit();break;
            case "MC":vm.MemoryClear();break;case "MR":vm.MemoryRecall();break;case "M+":vm.MemoryAdd();break;case "M-":vm.MemorySubtract();break;case "MS":vm.MemoryStore();break;
            default:if(key.Length==1&&(char.IsAsciiDigit(key[0])||key=="."))vm.InputDigit(key);else vm.ApplyScientificFunction(key);break;
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(new {display=vm.Display,expression=vm.Expression,scientific=vm.IsScientific,inverse=vm.IsInverse,hyp=vm.IsHyp,angle=vm.AngleMode.ToString(),has_memory=vm.HasMemory}));
}
// Localization is deliberately identity-only in this logic oracle. No UI or
// persistence is substituted; the production C# state machine is compiled as-is.
namespace DaisyOS.Shell.Apps.Calculator
{
    internal static class Loc { public static event EventHandler? Changed {add{} remove{}} public static string Get(string value)=>value; }
}

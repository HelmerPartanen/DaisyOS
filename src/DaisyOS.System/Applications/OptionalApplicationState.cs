using DaisyOS.System.Processes;
namespace DaisyOS.System.Applications;

/// <summary>Prevents development/bootstrap integration from recreating intentionally removed optional apps.</summary>
public static class OptionalApplicationState
{
    public static readonly string[] Applications = ["Files", "Mail", "Notes", "Calculator", "AppStore", "DiskManager", "UIKit"];
    private static string StatePath => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share"), "daisyos/applications/removed-optional-applications");
    private static string ShortId(string id) => DesktopApplicationMaskStore.NormalizeShortId(id).ToLowerInvariant();
    public static bool IsOptional(string id) => Applications.Any(app => app.Equals(ShortId(id),StringComparison.OrdinalIgnoreCase));
    public static bool IsRemoved(string id) => IsOptional(id)&&File.Exists(StatePath)&&File.ReadAllLines(StatePath).Contains(ShortId(id),StringComparer.OrdinalIgnoreCase);
    public static void SetRemoved(string id,bool removed)
    {
        if(!IsOptional(id))return;
        var values=File.Exists(StatePath)?File.ReadAllLines(StatePath).ToHashSet(StringComparer.OrdinalIgnoreCase):[];
        if(removed)values.Add(ShortId(id));else values.Remove(ShortId(id));
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temporary=StatePath+"."+Guid.NewGuid().ToString("N");File.WriteAllLines(temporary,values.Order());File.Move(temporary,StatePath,true);
    }
    public static bool IsPayloadAvailable(string id)
    {
        if(!IsOptional(id))return true;
        if(IsRemoved(id))return false;
        var name=Applications.Single(app=>app.Equals(ShortId(id),StringComparison.OrdinalIgnoreCase));
        var binary="daisyos-"+name.ToLowerInvariant();
        var output=new DirectoryInfo(AppContext.BaseDirectory);
        var checkout=output.Parent?.Parent?.Parent?.Parent;
        return File.Exists(Path.Combine(AppContext.BaseDirectory,"apps",name,"DaisyOS."+name))||
            File.Exists("/usr/bin/"+binary)||File.Exists("/usr/local/bin/"+binary)||File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".local/bin",binary))||
            checkout is not null&&File.Exists(Path.Combine(checkout.FullName,"DaisyOS."+name,"bin",Environment.GetEnvironmentVariable("DAISYOS_APP_CONFIGURATION")??"Release",output.Name,"DaisyOS."+name));
    }
}

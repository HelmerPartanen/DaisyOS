using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;
namespace DaisyOS.System.Applications;

public sealed class CompatibilityApplications(ICommandRunner runner, ApplicationRegistry registry)
{
    public static readonly string[] Images = ["docker.io/library/ubuntu:26.04", "registry.fedoraproject.org/fedora:45", "docker.io/library/ubuntu:24.04", "registry.fedoraproject.org/fedora:44"];
    public async Task<ApplicationTransactionPlan> InspectAsync(string path, string? selectedImage, CancellationToken token)
    {
        var deb=path.EndsWith(".deb",StringComparison.OrdinalIgnoreCase);
        var metadata=await LocalPackageMetadata.ReadAsync(path,runner,token).ConfigureAwait(false);
        var expected=RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => deb?"amd64":"x86_64", Architecture.Arm64=>deb?"arm64":"aarch64", _=>"unsupported" };
        var image=selectedImage??(deb?Images[0]:Images[1]);
        if(!Images.Contains(image)||deb!=image.Contains("ubuntu",StringComparison.Ordinal))throw new IOException("Choose a supported compatibility environment for this package format.");
        var suffix=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(metadata.Name+"|"+image))).ToLowerInvariant()[..16];
        var application=new InstalledApplication(new(ApplicationBackend.Container,metadata.Name,"user","daisy-app-"+suffix),metadata.Name,metadata.Version)
        {SourcePath=path,Architecture=metadata.Architecture,Image=image,DataDirectory=Path.Combine(registry.Root,"homes",suffix)};
        return new(ApplicationOperation.Install,application) { Error=metadata.Architecture!=expected&&metadata.Architecture is not ("all" or "noarch")
            ? $"This package is built for {metadata.Architecture}; this system requires {expected}." : "" };
    }
    private Task<CommandResult> Run(string command,IReadOnlyList<string> arguments,CancellationToken token)=>command=="distrobox"
        ? runner.RunAsync("env",["DBX_CONTAINER_MANAGER=podman","distrobox",..arguments],TimeSpan.FromMinutes(20),token)
        : runner.RunAsync(command,arguments,TimeSpan.FromMinutes(20),token);
    private static ApplicationTransactionResult Failed(CommandResult result)=>new(false,"Compatibility setup or package installation failed.",result.StandardError);
    public async Task<ApplicationTransactionResult> InstallAsync(InstalledApplication application,IProgress<ApplicationProgress>? progress,CancellationToken token)
    {
        foreach(var tool in new[]{"podman","distrobox"})
        {
            var check=await Run(tool,["--version"],token).ConfigureAwait(false);
            if(!check.Succeeded)return new(false,"Compatibility support needs Podman and Distrobox. Set up compatibility support before installing this app.",check.StandardError);
        }
        var container=application.Identity.Container!;
        var existing=registry.Read().FirstOrDefault(record=>record.Identity==application.Identity);
        var exists=await Run("podman",["container","exists",container],token).ConfigureAwait(false);
        if(exists.ExitCode is not (0 or 1))return Failed(exists);
        if(exists.Succeeded&&existing is null)return new(false,"A container with this name already exists but is not owned by DaisyOS. It was left untouched.");
        if(exists.Succeeded)
        {
            var label=await Run("podman",["inspect","--format","{{index .Config.Labels \"io.daisyos.application\"}}",container],token).ConfigureAwait(false);
            if(!label.Succeeded||label.StandardOutput.Trim()!=container)return new(false,"The existing compatibility environment no longer matches its ownership record. It was left untouched.");
        }
        var created=!exists.Succeeded;
        if (ApplicationRegistry.HasSymbolicAncestor(application.DataDirectory)) return new(false,"The compatibility data folder contains a symbolic link. Choose a regular folder.");
        Directory.CreateDirectory(application.DataDirectory);
        var temporary=Path.Combine(registry.Root,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
        var staged=Path.Combine(temporary,Path.GetFileName(application.SourcePath));
        var exported=new List<string>();
        try
        {
            File.Copy(application.SourcePath,staged);
            if(application.SourceFingerprint.Length>0)
            {
                await using var bytes=File.OpenRead(staged);
                var fingerprint=Convert.ToHexString(await SHA256.HashDataAsync(bytes,token).ConfigureAwait(false)).ToLowerInvariant();
                if(fingerprint!=application.SourceFingerprint)return new(false,"The package changed while being staged. Select it again.");
            }
            if(created)
            {
                progress?.Report(new("Downloading and preparing compatibility environment"));
                var create=await Run("distrobox",["create","--yes","--name",container,"--image",application.Image,"--home",application.DataDirectory,
                    "--additional-flags","--label io.daisyos.application="+container],token).ConfigureAwait(false);
                if(!create.Succeeded)return Failed(create);
            }
            var deb=staged.EndsWith(".deb",StringComparison.OrdinalIgnoreCase);
            // Read metadata again after copying; inspection and commit must refer to the same package identity.
            var metadata=await LocalPackageMetadata.ReadAsync(staged,runner,token).ConfigureAwait(false);
            if(metadata.Name!=application.Identity.PackageId||metadata.Version!=application.Version||metadata.Architecture!=application.Architecture)
                return new(false,"The package changed after inspection. Select it again before installing.");
            progress?.Report(new("Installing application and dependencies"));
            if(deb)
            {
                var update=await Enter(container,["sudo","apt-get","update"],token).ConfigureAwait(false);if(!update.Succeeded)return Failed(update);
            }
            var install=await Enter(container,deb?["sudo","env","DEBIAN_FRONTEND=noninteractive","apt-get","install","-y","--",staged]:["sudo","dnf","install","-y","--",staged],token).ConfigureAwait(false);
            if(!install.Succeeded)return Failed(install);
            var listing=await Enter(container,deb?["dpkg-query","-L",application.Identity.PackageId]:["rpm","-ql",application.Identity.PackageId],token).ConfigureAwait(false);
            if(!listing.Succeeded)return Failed(listing);
            var desktops=listing.StandardOutput.Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(path=>path.StartsWith("/usr/share/applications/",StringComparison.Ordinal)&&path.EndsWith(".desktop",StringComparison.Ordinal)).ToArray();
            foreach(var desktop in desktops)
            {
                var export=await Enter(container,["distrobox-export","--app",desktop],token).ConfigureAwait(false);if(!export.Succeeded)return Failed(export);
                // Distrobox prefixes exported launchers with the container name.
                var path=Path.Combine(DesktopApplicationMaskStore.UserApplicationsDirectory,container+"-"+Path.GetFileName(desktop));
                if(!File.Exists(path))return new(false,"The app installed, but its launcher could not be exported. Retry installation to repair it.");
                var lines=string.Join('\n',File.ReadAllLines(path).Select(line=>line.StartsWith("Exec=",StringComparison.Ordinal)?"Exec=env DBX_CONTAINER_MANAGER=podman "+line[5..]:line));File.WriteAllText(path,lines+"\nX-DaisyOS-Application="+ApplicationIntegration.IdentityTag(application.Identity)+"\n");exported.Add(path);
            }
            var image=await Run("podman",["inspect","--format","{{.Image}}",container],token).ConfigureAwait(false);
            registry.Save(application with { ImageDigest=image.Succeeded?image.StandardOutput.Trim():"",OwnedLaunchers=exported.ToArray(),
                OwnedCommands=ApplicationIntegration.Commands(exported),DesktopIds=exported.Select(Path.GetFileName).OfType<string>().ToArray() });
            created=false;return new(true,"Application installed in its compatibility environment.");
        }
        finally
        {
            if(created)
            {
                // Never delete a pre-existing or shared container. Cleanup itself is not cancelled.
                var cleanup=await Run("podman",["rm","--force",container],CancellationToken.None).ConfigureAwait(false);
                if(!cleanup.Succeeded) registry.Save(application with { IsBroken=true,OwnedLaunchers=exported.ToArray(),OwnedCommands=ApplicationIntegration.Commands(exported),DesktopIds=exported.Select(Path.GetFileName).OfType<string>().ToArray() });
                if(cleanup.Succeeded)
                    foreach(var launcher in exported)
                        if(File.Exists(launcher)&&!ApplicationRegistry.HasSymbolicAncestor(launcher)&&File.ReadAllLines(launcher).Contains("X-DaisyOS-Application="+ApplicationIntegration.IdentityTag(application.Identity))) File.Delete(launcher);
            }
            if(Directory.Exists(temporary))Directory.Delete(temporary,true);
        }
    }
    public async Task<ApplicationTransactionResult> RemoveAsync(InstalledApplication application,bool removeData,CancellationToken token)
    {
        var owned=registry.Read().FirstOrDefault(record=>record.Identity==application.Identity);
        if(owned is null)return new(false,"This compatibility environment has no ownership record. Use guided removal.");
        var container=owned.Identity.Container!;
        if(removeData&&ApplicationRegistry.HasSymbolicAncestor(owned.DataDirectory))return new(false,"The app’s saved-data path contains a symbolic link. Review it before deleting data.");
        var exists=await Run("podman",["container","exists",container],token).ConfigureAwait(false);
        if(exists.ExitCode is not (0 or 1))return Failed(exists);
        if(exists.Succeeded)
        {
            var check=await Run("podman",["inspect","--format","{{index .Config.Labels \"io.daisyos.application\"}}",container],token).ConfigureAwait(false);
            if(!check.Succeeded||check.StandardOutput.Trim()!=container)return new(false,"The container ownership could not be verified. Nothing was removed.",check.StandardError);
            var result=await Run("podman",["rm","--force",container],token).ConfigureAwait(false);if(!result.Succeeded)return Failed(result);
        }
        if(removeData&&ApplicationRegistry.IsWithin(owned.DataDirectory,Path.Combine(registry.Root,"homes"))&&Directory.Exists(owned.DataDirectory)&&!ApplicationRegistry.HasSymbolicAncestor(owned.DataDirectory))
            Directory.Delete(owned.DataDirectory,true);
        return new(true,"Compatibility application uninstalled.");
    }
    private Task<CommandResult> Enter(string container,IReadOnlyList<string> command,CancellationToken token)=>Run("distrobox",["enter","--name",container,"--",..command],token);
}

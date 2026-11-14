using DaisyOS.Core.Models;
using DaisyOS.System.Applications;
using DaisyOS.System.Processes;
using Xunit;
namespace DaisyOS.Applications.Tests;

public sealed class ApplicationManagementTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"daisy-applications-test-"+Guid.NewGuid().ToString("N"));
    public ApplicationManagementTests()=>Directory.CreateDirectory(_root);
    public void Dispose(){if(Directory.Exists(_root))Directory.Delete(_root,true);}
    private sealed class Runner(Func<string,IReadOnlyList<string>,CommandResult> execute):ICommandRunner
    {
        public List<(string Command,string[] Arguments)> Calls {get;}=[];
        public Task<CommandResult> RunAsync(string command,IReadOnlyList<string> args,TimeSpan timeout,CancellationToken cancellationToken=default)
        {Calls.Add((command,args.ToArray()));return Task.FromResult(execute(command,args));}
    }
    [Fact]
    public async Task ProtectedTransactionsNeverAuthenticateOrCommit()
    {
        var runner=new Runner((_,_)=>CommandResult.Success("{\"packages\":[\"remove:kwin=1\"],\"protected\":true,\"error\":\"Essential desktop component\"}"));
        var service=new ApplicationManagementService(runner,_root);
        var plan=await service.PrepareUninstallAsync(new(new(ApplicationBackend.Pacman,"kwin","system"),"KWin"));
        Assert.False(plan.CanExecute);
        Assert.False((await service.ExecuteAsync(plan)).Succeeded);
        Assert.DoesNotContain(runner.Calls,call=>call.Command=="pkexec");
    }
    [Fact]
    public async Task ChangedPackagePlanRequiresAnotherReview()
    {
        var version=1;
        var runner=new Runner((_,_)=>CommandResult.Success("{\"packages\":[\"remove:example="+(version++)+"\"]}"));
        var service=new ApplicationManagementService(runner,_root);
        var plan=await service.PrepareUninstallAsync(new(new(ApplicationBackend.Pacman,"example","system"),"Example"));
        var result=await service.ExecuteAsync(plan);
        Assert.False(result.Succeeded);Assert.Contains("changed",result.Message);
        Assert.DoesNotContain(runner.Calls,call=>call.Command=="pkexec");
    }
    [Fact]
    public async Task FailedRemovalKeepsShortcutsAndTheirContents()
    {
        var directory=Path.Combine(_root,"applications");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"renamed.desktop");File.WriteAllText(path,"[Desktop Entry]\nExec=example\n");
        var runner=new Runner((_,_)=>CommandResult.Failed("Authentication cancelled",126));
        var service=new ApplicationManagementService(runner,_root);
        var app=new InstalledApplication(new(ApplicationBackend.Flatpak,"org.example.App","system"),"Example") {OwnedCommands=["Exec=example"]};
        Assert.False((await service.ExecuteAsync(new(ApplicationOperation.Uninstall,app))).Succeeded);
        Assert.True(File.Exists(path));Assert.Contains("Exec=example",File.ReadAllText(path));
    }
    [Theory]
    [InlineData("user","--user")]
    [InlineData("system","--system")]
    [InlineData("work","--installation=work")]
    public async Task FlatpakRemovalUsesExactScopeAndOnlyDeletesItsOwnRenamedLauncher(string scope,string argument)
    {
        var directory=Path.Combine(_root,"applications");Directory.CreateDirectory(directory);
        var identity=new ApplicationIdentity(ApplicationBackend.Flatpak,"org.example.App",scope);
        var selected=Path.Combine(directory,"renamed.desktop");
        File.WriteAllText(selected,"[Desktop Entry]\nX-DaisyOS-Application="+ApplicationIntegration.IdentityTag(identity)+"\n");
        var other=Path.Combine(directory,"keep.desktop");File.WriteAllText(other,"[Desktop Entry]\nX-DaisyOS-Application="+ApplicationIntegration.IdentityTag(identity with {Scope="another"})+"\n");
        var runner=new Runner((_,args)=>args[0]=="info"?CommandResult.Failed("Not installed"):CommandResult.Success());
        var service=new ApplicationManagementService(runner,_root);
        var app=new InstalledApplication(identity,"Example"){DesktopIds=["org.example.App.desktop"]};
        var result=await service.ExecuteAsync(new(ApplicationOperation.Uninstall,app));
        Assert.True(result.Succeeded,result.Details);Assert.False(File.Exists(selected));Assert.True(File.Exists(other));
        Assert.Contains(runner.Calls,call=>call.Command=="flatpak"&&call.Arguments.SequenceEqual(new[]{"uninstall",argument,"-y","--noninteractive","--","org.example.App"}));
    }
    [Fact]
    public async Task GenericLauncherIsKeptWhenAnotherFlatpakScopeStillExists()
    {
        var directory=Path.Combine(_root,"applications");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"renamed.desktop");File.WriteAllText(path,"[Desktop Entry]\nExec=flatpak run org.example.App\n");
        var runner=new Runner((_,args)=>args[0]=="info"?CommandResult.Failed("Not installed"):CommandResult.Success(args[0]=="list"&&!args.Contains("--user")?(args.Contains("--columns=application")?"org.example.App\n":"org.example.App\tsystem\n"):""));
        var app=new InstalledApplication(new(ApplicationBackend.Flatpak,"org.example.App"),"Example"){OwnedCommands=["Exec=flatpak run org.example.App"]};
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app));
        Assert.True(result.Succeeded);Assert.True(File.Exists(path));Assert.Empty(result.RemovedDesktopIds);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContainerUninstallRetainsDataUnlessExplicitlyRequested(bool removeData)
    {
        var registry=new ApplicationRegistry(_root);var data=Path.Combine(registry.Root,"homes","example");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"preferences"),"keep me");
        var app=new InstalledApplication(new(ApplicationBackend.Container,"example","user","daisy-app-example"),"Example"){DataDirectory=data};registry.Save(app);
        var runner=new Runner((_,args)=>CommandResult.Success(args[0]=="inspect"?"daisy-app-example\n":""));
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app),removeData);
        Assert.True(result.Succeeded,result.Details);Assert.Equal(!removeData,Directory.Exists(data));Assert.Empty(registry.Read());
    }
    [Fact]
    public async Task ForeignContainerCannotBeRemoved()
    {
        var registry=new ApplicationRegistry(_root);var app=new InstalledApplication(new(ApplicationBackend.Container,"example","user","daisy-app-example"),"Example");registry.Save(app);
        var runner=new Runner((_,_)=>CommandResult.Success("someone-else"));
        Assert.False((await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app))).Succeeded);
        Assert.DoesNotContain(runner.Calls,call=>call.Arguments[0]=="rm");Assert.Single(registry.Read());
    }
    [Fact]
    public void OwnershipRegistryCorruptionFailsClosedAndDoesNotEraseFile()
    {
        var registry=new ApplicationRegistry(_root);Directory.CreateDirectory(registry.Root);var path=Path.Combine(registry.Root,"installations.json");File.WriteAllText(path,"broken");
        Assert.Throws<global::System.Text.Json.JsonException>(()=>registry.Read());Assert.Equal("broken",File.ReadAllText(path));
    }
    [Fact]
    public void FingerprintIsStableAcrossOrderingButChangesWithVersions()
    {
        Assert.Equal(ApplicationManagementService.TransactionFingerprint(["b=1","a=1"]),ApplicationManagementService.TransactionFingerprint(["a=1","b=1"]));
        Assert.NotEqual(ApplicationManagementService.TransactionFingerprint(["a=1"]),ApplicationManagementService.TransactionFingerprint(["a=2"]));
    }
    [Fact]
    public async Task MalformedPackagesAreNeverExecuted()
    {
        var path=Path.Combine(_root,"example.rpm");File.WriteAllBytes(path,new byte[128]);var runner=new Runner((_,_)=>throw new Xunit.Sdk.XunitException("Package code must not execute during inspection."));
        await Assert.ThrowsAsync<IOException>(()=>LocalPackageMetadata.ReadAsync(path,runner,CancellationToken.None));Assert.Empty(runner.Calls);
    }
    [Fact]
    public async Task UnmanagedRemovalRequiresExplicitFolderReview()
    {
        var runner=new Runner((_,_)=>throw new Xunit.Sdk.XunitException("No command may run before review."));
        var service=new ApplicationManagementService(runner,_root);
        var plan=await service.PrepareUninstallAsync(new(new(ApplicationBackend.Unmanaged,"example"),"Example"));
        Assert.False(plan.CanExecute);Assert.True(plan.RequiresReview);
        Assert.False((await service.ExecuteAsync(plan)).Succeeded);Assert.Empty(runner.Calls);
    }
    [Fact]
    public async Task ChangedLocalPackageCannotReachThePrivilegedHelper()
    {
        var path=Path.Combine(_root,"example.pkg.tar.zst");File.WriteAllText(path,"changed");
        var runner=new Runner((_,_)=>throw new Xunit.Sdk.XunitException("Changed package must not be committed."));
        var app=new InstalledApplication(new(ApplicationBackend.Pacman,"example","system"),"Example"){SourcePath=path,SourceFingerprint="original"};
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Install,app));
        Assert.False(result.Succeeded);Assert.Contains("changed",result.Message);Assert.Empty(runner.Calls);
    }
    [Fact]
    public async Task SharedFlatpakDataCannotBeDeletedAfterAnotherInstallationAppears()
    {
        var runner=new Runner((_,_)=>CommandResult.Success("org.example.App\tuser\tapp/org.example.App/x86_64/stable\norg.example.App\tsystem\tapp/org.example.App/x86_64/stable\n"));
        var app=new InstalledApplication(new(ApplicationBackend.Flatpak,"org.example.App"),"Example");
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app),removeData:true);
        Assert.False(result.Succeeded);Assert.DoesNotContain(runner.Calls,call=>call.Arguments[0]=="uninstall");
    }
    [Fact]
    public async Task IconCleanupDoesNotFollowASymbolicParent()
    {
        var sentinel=Path.Combine(_root,"outside");Directory.CreateDirectory(sentinel);var icon=Path.Combine(sentinel,"keep.png");File.WriteAllText(icon,"keep");
        Directory.CreateSymbolicLink(Path.Combine(_root,"icons"),sentinel);
        var runner=new Runner((_,_)=>CommandResult.Success());
        await new ApplicationIntegration(runner,_root).RemoveAsync(new(new(ApplicationBackend.AppImage,"example"),"Example"){OwnedIcons=[Path.Combine(_root,"icons/keep.png")]});
        Assert.True(File.Exists(icon));
    }

    [Fact]
    public async Task RenamedFlatpakShortcutIsMatchedByItsExportedApplicationIdentity()
    {
        var directory=Path.Combine(_root,"applications");Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"My renamed browser.desktop");File.WriteAllText(path,"[Desktop Entry]\nX-Flatpak=org.example.App\nExec=/usr/bin/flatpak run org.example.App\n");
        var runner=new Runner((_,_)=>CommandResult.Success());
        var app=new InstalledApplication(new(ApplicationBackend.Flatpak,"org.example.App"),"Example"){DesktopIds=["org.example.App.desktop"]};
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app));
        Assert.True(result.Succeeded);Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DebianInspectionReadsMetadataWithoutRunningMaintainerScripts()
    {
        var sentinel=Path.Combine(_root,"must-not-run");
        var archive=Path.Combine(_root,"control.tar");
        await using(var output=File.Create(archive))
        using(var writer=new global::System.Formats.Tar.TarWriter(output,leaveOpen:true))
        {
            foreach(var (name,content) in new[]{("./control","Package: example-editor\nVersion: 2.0\nArchitecture: all\n"),("./postinst","#!/bin/sh\ntouch "+sentinel+"\n")})
            {
                using var bytes=new MemoryStream(global::System.Text.Encoding.UTF8.GetBytes(content));
                writer.WriteEntry(new global::System.Formats.Tar.PaxTarEntry(global::System.Formats.Tar.TarEntryType.RegularFile,name){DataStream=bytes});
            }
        }
        var package=Path.Combine(_root,"example.deb");
        using(var output=File.Create(package))
        {
            output.Write(global::System.Text.Encoding.ASCII.GetBytes("!<arch>\n"));
            foreach(var (name,bytes) in new[]{("debian-binary",global::System.Text.Encoding.ASCII.GetBytes("2.0\n")),("control.tar",File.ReadAllBytes(archive))})
            {
                var header=name.PadRight(16)+"0".PadRight(12)+"0".PadRight(6)+"0".PadRight(6)+"100644".PadRight(8)+bytes.Length.ToString().PadRight(10)+"`\n";
                output.Write(global::System.Text.Encoding.ASCII.GetBytes(header));output.Write(bytes);if(bytes.Length%2!=0)output.WriteByte((byte)'\n');
            }
        }
        var metadata=await LocalPackageMetadata.ReadAsync(package,new SafeCommandRunner(),CancellationToken.None);
        Assert.Equal(new LocalPackageMetadata("example-editor","2.0","all"),metadata);Assert.False(File.Exists(sentinel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppImageRemovalDeletesOwnedBinaryAtEitherLocationAndPreservesOtherFiles(bool custom)
    {
        var data=Path.Combine(_root,"data");var registry=Path.Combine(data,"daisyos/appimages");Directory.CreateDirectory(registry);
        var folder=custom?Path.Combine(_root,"custom"):Path.Combine(registry,"example");Directory.CreateDirectory(folder);
        var binary=Path.Combine(folder,"example.AppImage");File.WriteAllText(binary,"binary");
        var document=Path.Combine(folder,"document.txt");File.WriteAllText(document,"keep");
        var original=Path.Combine(_root,"download.AppImage");File.WriteAllText(original,"original");
        var apps=Path.Combine(data,"applications");Directory.CreateDirectory(apps);var launcher=Path.Combine(apps,"example.desktop");File.WriteAllText(launcher,"[Desktop Entry]\nX-DaisyOS-AppImage=example\n");
        File.WriteAllText(Path.Combine(registry,"installed.json"),global::System.Text.Json.JsonSerializer.Serialize(new { installations=new[] {new {desktopId="example",name="Example",appImagePath=binary,originalSourcePath=original,desktopFilePath=launcher,iconPath=""}} }));
        var runner=new Runner((_,_)=>CommandResult.Success());
        Assert.True(await new DaisyOS.System.AppImage.AppImageService(runner,data).UninstallAsync("example"));
        Assert.False(File.Exists(binary));Assert.False(File.Exists(launcher));Assert.True(File.Exists(original));Assert.True(File.Exists(document));
    }
    [Fact]
    public async Task BrokenAppImageRegistryDoesNotBecomeAnEmptyOwnershipDatabase()
    {
        var folder=Path.Combine(_root,"daisyos/appimages");Directory.CreateDirectory(folder);var file=Path.Combine(folder,"installed.json");File.WriteAllText(file,"invalid");
        await Assert.ThrowsAsync<IOException>(()=>new DaisyOS.System.AppImage.AppImageService(dataHome:_root).GetInstalledAsync());
        Assert.Equal("invalid",File.ReadAllText(file));
    }

    [Fact]
    public async Task AlreadyDeletedOwnedContainerCanStillBeCleanedUp()
    {
        var registry=new ApplicationRegistry(_root);
        var app=new InstalledApplication(new(ApplicationBackend.Container,"example","user","daisy-app-example"),"Example");registry.Save(app);
        var runner=new Runner((_,args)=>args.Contains("exists")?CommandResult.Failed("not found",1):CommandResult.Success());
        var result=await new ApplicationManagementService(runner,_root).ExecuteAsync(new(ApplicationOperation.Uninstall,app));
        Assert.True(result.Succeeded);Assert.Empty(registry.Read());Assert.DoesNotContain(runner.Calls,call=>call.Arguments[0]=="rm");
    }
    [Fact]
    public async Task AlreadyDeletedRegisteredDevelopmentPayloadOffersShortcutCleanup()
    {
        var registry=new ApplicationRegistry(_root);
        var app=new InstalledApplication(new(ApplicationBackend.Unmanaged,"org.daisyos.notes.desktop"),"Notes"){ReviewedPaths=[Path.Combine(_root,"missing-build-output")]};registry.Save(app);
        var runner=new Runner((_,_)=>CommandResult.Success());var service=new ApplicationManagementService(runner,_root);
        var plan=await service.PrepareUninstallAsync(app);Assert.True(plan.CleanupOnly);Assert.True(plan.CanExecute);
    }

}

using System.Collections.Generic;
using System.Text.Json.Serialization;
using DaisyOS.System.AppImage;
using DaisyOS.System.Networking.LocalSend;
using DaisyOS.System.Policies;

namespace DaisyOS.System.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(LocalSendService.MulticastMessage))]
[JsonSerializable(typeof(LocalSendService.RegisterInfo))]
[JsonSerializable(typeof(LocalSendService.RegisterResponse))]
[JsonSerializable(typeof(LocalSendService.FileInfoDto))]
[JsonSerializable(typeof(LocalSendService.PrepareUploadRequest))]
[JsonSerializable(typeof(LocalSendService.PrepareUploadResponse))]
[JsonSerializable(typeof(LocalSendService.LocalSendErrorMessage))]
[JsonSerializable(typeof(JsonAppPolicyService.AppPolicyDocument))]
[JsonSerializable(typeof(JsonAppPolicyService.AppPolicyEntryDocument))]
[JsonSerializable(typeof(AppImageRegistryDocument))]
[JsonSerializable(typeof(List<DaisyOS.Core.Models.InstalledApplication>))]
[JsonSerializable(typeof(DaisyOS.System.Applications.NativeTransaction))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, LocalSendService.FileInfoDto>))]
internal sealed partial class DaisySystemJsonContext : JsonSerializerContext
{
}

